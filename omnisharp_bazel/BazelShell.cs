// Bazel Project System for OmniSharp
// https://github.com/msaville128/omnisharp_bazel

using System;
using System.Composition;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OmniSharp.Bazel;

/// <summary>
/// An abstraction of the system shell for executing Bazel commands.
/// </summary>
[Export, Shared]
[method: ImportingConstructor]
public class BazelShell
    (
        IOmniSharpEnvironment environment,
        ILoggerFactory loggerFactory
    )
{
    // Most users will have bazelisk installed so this is the sensible default.
    const string DefaultExecutable = "bazelisk";

    readonly ILogger logger = loggerFactory.CreateLogger<BazelShell>();

    /// <summary>
    /// The system name or path of the Bazel executable to use.
    /// </summary>
    public string Executable { get; set; } = DefaultExecutable;

    /// <summary>
    /// Runs the given command using the Bazel executable, and returns the whole
    /// standard output or an empty string if there was an error.
    /// </summary>
    public async Task<string> RunAsync(string command)
    {
        using var process = StartProcess(command);
        if (process is null)
        {
            logger.LogError("Failed to run {executable}", Executable);
            return "";
        }

        logger.LogInformation("{executable} {command}", Executable, command);

        // Read both outputs at the same time to avoid deadlock.
        var output = await Task.WhenAll(
            process.StandardOutput.ReadToEndAsync(),
            process.StandardError.ReadToEndAsync());

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            // Bazel logs to stderr so this will include info and error logs.
            logger.LogError("{error}", output[1]);
            return "";
        }

        return output[0]; // stdout
    }

    Process? StartProcess(string command)
    {
        string shell;
        string shellFlags;

        if (OperatingSystem.IsWindows())
        {
            shell = "cmd.exe";
            shellFlags = "/c";
        }
        else
        {
            shell = "/bin/sh";
            shellFlags = "-c";
        }

        static string escape(string value) => value.Replace("\"", "\\\"");
        string arguments = $"{shellFlags} \"{Executable} {escape(command)}\"";

        return Process.Start(
            new ProcessStartInfo
            {
                WorkingDirectory = environment.TargetDirectory,
                FileName = shell,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
    }
}
