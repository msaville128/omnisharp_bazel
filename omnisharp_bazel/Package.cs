// Bazel Project System for OmniSharp
// https://github.com/msaville128/omnisharp_bazel

using System.Collections.Immutable;
using System.IO;

namespace OmniSharp.Bazel;

/// <summary>
/// A package in a Bazel repo. Packages are defined with BUILD files.
/// </summary>
public readonly record struct Package(string FilePath)
{
    // Order is important! The file name with extension must take precedence.
    static readonly ImmutableArray<string> FileNames = ["BUILD.bazel", "BUILD"];

    /// <summary>
    /// Recursively searches for a package starting in the provided directory.
    /// </summary>
    public static bool TryFind(string? path, out Package package)
    {
        if (string.IsNullOrEmpty(path))
        {
            package = default;
            return false;
        }

        foreach (string fileName in FileNames)
        {
            string candidate = Path.Combine(path, fileName);
            if (File.Exists(candidate))
            {
                package = new Package(candidate);
                return true;
            }
        }

        string? parent = Path.GetDirectoryName(path);
        return TryFind(parent, out package);
    }

    /// <summary>
    /// Determines whether the provided path points to a BUILD file.
    /// </summary>
    public static bool IsPackage(string path)
    {
        return FileNames.Contains(Path.GetFileName(path));
    }
}
