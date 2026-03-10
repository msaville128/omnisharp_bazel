// Bazel Project System for OmniSharp
// https://github.com/msaville128/omnisharp_bazel

using System.IO;

namespace OmniSharp.Bazel;

/// <summary>
/// A document in the main repo. Each document is associated with a package.
/// </summary>
public readonly record struct Document(Package Package, string DocumentPath)
{
    /// <summary>
    /// <c>true</c> if this document is a BUILD file.
    /// </summary>
    public bool IsPackage { get; } = Package.FilePath.Equals(DocumentPath);

    /// <summary>
    /// Creates an instance only if a package that owns it is found.
    /// </summary>
    public static bool TryCreate(string path, out Document document)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!Package.TryFind(directory, out Package package))
        {
            document = default;
            return false;
        }

        document = new Document(package, path);
        return true;
    }

    /// <summary>
    /// Generates a label for this document as a file target in the main repo.
    /// </summary>
    public Label ToLabel(string repoPath)
    {
        string directory = Path.GetDirectoryName(Package.FilePath) ?? "";
        string relativeDirectory = Path.GetRelativePath(repoPath, directory);
        string relativeFilePath = Path.GetRelativePath(directory, DocumentPath);

        static string normalize(string value) => value.Trim('/', '.');
        string packageName = normalize(relativeDirectory);
        string targetName = normalize(relativeFilePath);

        return new Label($"@@//{packageName}:{targetName}");
    }
}
