// Bazel Project System for OmniSharp
// https://github.com/msaville128/omnisharp_bazel

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;

using static System.StringSplitOptions;

namespace OmniSharp.Bazel;

/// <summary>
/// The manager of C# projects within the repo.
/// </summary>
[Export, Shared]
[method: ImportingConstructor]
public class BazelProjectManager
    (
        ILoggerFactory loggerFactory,
        OmniSharpWorkspace workspace,
        IOmniSharpEnvironment environment,
        BazelShell bazel
    )
{
    // One of the target's build actions must include a path to this binary in
    // order to be loaded as a C# project.
    static readonly ImmutableArray<string> CSharpCompiler = ["csc.dll", "csc.exe"];

    // Bazel server does not support concurrent commands.
    readonly SemaphoreSlim signal = new(1);

    readonly Dictionary<Label, ProjectId> targetsToProjects = [];
    readonly Dictionary<Document, HashSet<Label>> documentsToTargets = [];

    readonly ILogger logger = loggerFactory.CreateLogger<BazelProjectManager>();

    /// <summary>
    /// Notifies that a document may have been created or changed.
    /// </summary>
    public async Task NotifyDocumentAsync(Document document)
    {
        await signal.WaitAsync();
        try
        {
            if (document.IsPackage)
            {
                await InvalidatePackageAsync(document.Package, reprocess: true);
            }
            else if (!IsDocumentTracked(document))
            {
                await ProcessDocumentAsync(document);
            }
        }
        finally
        {
            signal.Release();
        }
    }

    /// <summary>
    /// Notifies that a document or directory may have been deleted.
    /// </summary>
    public async Task NotifyDeletionAsync(string path)
    {
        await signal.WaitAsync();
        try
        {
            if (Document.TryCreate(path, out Document document))
            {
                if (document.IsPackage)
                {
                    await InvalidatePackageAsync(document.Package, reprocess: false);
                }
                else if (IsDocumentTracked(document))
                {
                    RemoveDocument(document);
                }
            }

            // Path could be a directory.
            await RemoveDirectoryAsync(path);
        }
        finally
        {
            signal.Release();
        }
    }

    async Task RemoveDirectoryAsync(string path)
    {
        string prefix = path.TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var documents = documentsToTargets.Keys
            .Where(d => d.DocumentPath.StartsWith(prefix))
            .ToList();

        foreach (Document document in documents)
        {
            if (document.IsPackage)
            {
                await InvalidatePackageAsync(document.Package, reprocess: false);
            }
            else if (IsDocumentTracked(document))
            {
                RemoveDocument(document);
            }
        }
    }

    void RemoveDocument(Document document)
    {
        if (!documentsToTargets.Remove(document, out HashSet<Label>? targets))
        {
            return;
        }

        foreach (Label target in targets)
        {
            if (targetsToProjects.TryGetValue(target, out ProjectId? projectId))
            {
                var project = workspace.CurrentSolution.GetProject(projectId);

                var documentId = project?.Documents
                    .Where(d => d.FilePath == document.DocumentPath)
                    .Select(d => d.Id)
                    .FirstOrDefault();

                if (documentId is not null)
                {
                    workspace.RemoveDocument(documentId);
                }
            }
        }
    }

    async Task InvalidatePackageAsync(Package package, bool reprocess)
    {
        HashSet<Label> staleTargets = [];
        List<Document> staleDocuments = [];

        foreach (var (document, targets) in documentsToTargets)
        {
            if (document.Package == package)
            {
                staleDocuments.Add(document);
                staleTargets.UnionWith(targets);
            }
        }

        foreach (Label target in staleTargets)
        {
            if (targetsToProjects.Remove(target, out ProjectId? projectId))
            {
                workspace.RemoveProject(projectId);
                logger.LogInformation("Removed {target}", target);
            }
        }

        foreach (Document document in staleDocuments)
        {
            documentsToTargets.Remove(document);
        }

        if (reprocess)
        {
            foreach (Document document in staleDocuments)
            {
                if (!document.IsPackage)
                {
                    await ProcessDocumentAsync(document);
                }
            }
        }
    }

    async Task ProcessDocumentAsync(Document document)
    {
        Label fileTarget = document.ToLabel(environment.TargetDirectory);

        HashSet<Label> referencingTargets = [];
        foreach (Label target in await FindReferencesToTargetAsync(fileTarget))
        {
            referencingTargets.Add(target);

            if (TryGetProject(target, out ProjectId? projectId))
            {
                AddDocumentToProject(document, projectId);
            }
            else
            {
                await CreateProjectForTargetAsync(target);
            }
        }

        TrackDocument(document, referencingTargets);
    }

    async Task<IEnumerable<Label>> FindReferencesToTargetAsync(Label target)
    {
        var command = $"query 'rdeps(//..., {target.Id}) except {target.Id}'";

        return (await bazel.RunAsync(command))
            .Split('\n', TrimEntries | RemoveEmptyEntries)
            .Select(label => new Label(label));
    }

    bool IsDocumentTracked(Document document)
    {
        return documentsToTargets.ContainsKey(document);
    }

    void TrackDocument(Document document, HashSet<Label> referencingTargets)
    {
        if (!documentsToTargets.TryGetValue(document, out HashSet<Label>? targets))
        {
            targets = referencingTargets;
            documentsToTargets[document] = targets;
        }

        targets.UnionWith(referencingTargets);
    }

    bool TryGetProject(Label target, [NotNullWhen(true)] out ProjectId? projectId)
    {
        return targetsToProjects.TryGetValue(target, out projectId);
    }

    async Task<CSharpCommandLineArguments?> FindCSharpArgumentsAsync(Label target)
    {
        // Iterate through each build command to look for C# compiler arguments.
        return (await bazel.RunAsync($"aquery {target.Id} --output=commands"))
            .Split('\n')
            .Select(command => command
                .Split(' ', RemoveEmptyEntries)
                .SkipWhile(arg => !CSharpCompiler.Any(csc => arg.EndsWith("/" + csc)))
                .Skip(1))
            .Where(args => args.Any())
            .Select(args => CSharpCommandLineParser.Default
                .Parse(args, environment.TargetDirectory, sdkDirectory: null))
            .FirstOrDefault();
    }

    async Task CreateProjectForTargetAsync(Label target)
    {
        var csharpArguments = await FindCSharpArgumentsAsync(target);
        if (csharpArguments is null)
        {
            // No-op because the target isn't a C# library or binary.
            return;
        }

        var sourcePaths = csharpArguments.SourceFiles
            .Select(file => Path.Combine(environment.TargetDirectory, file.Path))
            .Select(Path.GetFullPath);

        foreach (string sourcePath in sourcePaths)
        {
            if (Document.TryCreate(sourcePath, out Document document))
            {
                TrackDocument(document, [target]);
            }
        }

        if (!target.TryFindPackage(environment.TargetDirectory, out var package))
        {
            return;
        }

        var projectId = ProjectId.CreateNewId(target.Id);
        var project = ProjectInfo
            .Create
            (
                projectId,
                VersionStamp.Default,
                name: target.Id,
                assemblyName: $"{target.Name}.dll",
                language: "C#",
                filePath: package.FilePath
            )
            .WithCompilationOptions(csharpArguments.CompilationOptions)
            .WithDocuments(sourcePaths
                .Select(path => CreateDocumentInfo(projectId, path)))
            .WithMetadataReferences(csharpArguments.MetadataReferences
                .Select(metadata => CreateMetadataReference(metadata.Reference)));

        targetsToProjects[target] = projectId;
        workspace.AddProject(project);

        logger.LogInformation("Added {target}", target);
    }

    void AddDocumentToProject(Document document, ProjectId projectId)
    {
        Project? project = workspace.CurrentSolution.GetProject(projectId);
        if (project is null)
        {
            return;
        }

        if (project.Documents.Any(d => d.FilePath == document.DocumentPath))
        {
            return;
        }

        workspace.AddDocument(projectId, document.DocumentPath);
    }

    DocumentInfo CreateDocumentInfo(ProjectId projectId, string path)
    {
        return DocumentInfo.Create
        (
            id: DocumentId.CreateNewId(projectId),
            name: Path.GetFileName(path),
            loader: new FileTextLoader(path, Encoding.UTF8),
            filePath: path
        );
    }

    MetadataReference CreateMetadataReference(string path)
    {
        string actualPath;
        if (path.StartsWith("bazel-out/"))
        {
            // Assembly is from the local repo.
            actualPath = Path.Combine(environment.TargetDirectory, path);
        }
        else
        {
            // Assembly is from an external repo.
            actualPath = Path.Combine(Path.GetFullPath(GetOutputBase()), path);
        }

        return MetadataReference
            .CreateFromFile(actualPath, documentation: FindDocumentation(actualPath));
    }

    string GetOutputBase()
    {
        string bazelOut = Path.Combine(environment.TargetDirectory, "bazel-out");

        // bazel-out is a symlink, resolve the actual path.
        string? resolvedBazelOut = new DirectoryInfo(bazelOut)
            .ResolveLinkTarget(true)?.FullName ?? bazelOut;

        string outputBase = Path.Combine(resolvedBazelOut, "..", "..", "..");
        return Path.GetFullPath(outputBase);
    }

    DocumentationProvider FindDocumentation(string path)
    {
        string directory = Path.GetDirectoryName(path) ?? "";
        string assemblyName = Path.GetFileNameWithoutExtension(path);

        // Documentation is usually in same directory for external assemblies
        // but in the parent directory for assemblies from the local repo.
        string[] candidates =
        [
            Path.Combine(directory, $"{assemblyName}.xml"),
            Path.Combine(directory, "..", $"{assemblyName}.xml")
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return XmlDocumentationProvider.CreateFromFile(candidate);
            }
        }

        return DocumentationProvider.Default;
    }
}
