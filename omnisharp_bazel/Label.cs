// Bazel Project System for OmniSharp
// https://github.com/msaville128/omnisharp_bazel

using System.IO;

namespace OmniSharp.Bazel;

/// <summary>
/// A label that identifies a target in a Bazel repo.
/// </summary>
public readonly record struct Label(string Id)
{
    /// <summary>
    /// The name component of the label.
    /// </summary>
    public string Name { get; } = Id.Split(':', count: 2)[1];

    /// <summary>
    /// Attempts to get the package for the label.
    /// </summary>
    public bool TryFindPackage(string repoPath, out Package package)
    {
        string packageId = Id.Split(':', count: 2)[0].TrimStart('/');
        string packagePath = Path.Combine(repoPath, packageId);

        if (!Package.TryFind(packagePath, out package))
        {
            return false;
        }

        return true;
    }

    public override string ToString()
    {
        return Id;
    }
}
