// Bazel Project System for OmniSharp
// https://github.com/msaville128/omnisharp_bazel

using System.Composition;
using System.Threading.Tasks;

namespace OmniSharp.Bazel;

/// <summary>
/// A document source that notifies the Bazel Project Manager of documents when
/// they are opened in an editor.
/// </summary>
[Export, Shared]
[method: ImportingConstructor]
public class OnDemandSource
    (
        OmniSharpWorkspace workspace,
        BazelProjectManager manager
    )
{
    /// <summary>
    /// Begins monitoring for opened documents from the OmniSharp client.
    /// </summary>
    public void Init()
    {
        workspace.AddWaitForProjectModelReadyHandler(OnOpenedAsync);
    }

    async Task OnOpenedAsync(string documentPath)
    {
        if (!Document.TryCreate(documentPath, out Document document))
        {
            return;
        }

        await manager.NotifyDocumentAsync(document);
    }
}
