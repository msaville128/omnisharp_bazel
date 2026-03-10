// Bazel Project System for OmniSharp
// https://github.com/msaville128/omnisharp_bazel

using System.Collections.Generic;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OmniSharp.Mef;
using OmniSharp.Models.WorkspaceInformation;
using OmniSharp.Services;

namespace OmniSharp.Bazel;

/// <summary>
/// A plugin that converts C# Bazel targets into .NET Compiler Platform (Roslyn)
/// projects for IDE integration.
/// </summary>
[ExportProjectSystem(nameof(BazelProjectSystem))]
[method: ImportingConstructor]
public class BazelProjectSystem
    (
        BazelShell bazel,
        FileSystemSource fileSystemSource,
        OnDemandSource onDemandSource
    )
    : IProjectSystem
{
    public string Key { get; } = "Bazel";
    public string Language { get; } = "C#";
    public IEnumerable<string> Extensions { get; } = [".cs"];
    public bool EnabledByDefault { get; } = true;
    public bool Initialized { get; private set; }

    public void Initalize(IConfiguration config)
    {
        if (Initialized) return;
        Initialized = true;

        if (config.GetValue<string?>("executable") is string executable)
        {
            bazel.Executable = executable;
        }

        fileSystemSource.Init();
        onDemandSource.Init();
    }

    public Task WaitForIdleAsync()
    {
        // No-op because initialization doesn't do any I/O.
        return Task.CompletedTask;
    }

    public Task<object> GetProjectModelAsync(string filePath)
    {
        // Not required but should return MSBuildProjectInfo model for compat?
        return Task.FromResult((object)new { });
    }

    public Task<object> GetWorkspaceModelAsync(WorkspaceInformationRequest request)
    {
        // Not required but should return MSBuildWorkspaceInfo model for compat?
        return Task.FromResult((object)new { });
    }
}
