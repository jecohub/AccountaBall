using AccountaBall.App.Shell;
using AccountaBall.Core.Models;
using Microsoft.UI.Xaml.Controls;

namespace AccountaBall.App.Views;

/// The guided "needs Ollama" card. Port of AiUnavailableView (design §6.2): show
/// the hint, a Download link, and a Retry that re-probes the provider.
public sealed class AiUnavailableView : UserControl, IPhaseView
{
    public void Bind(AppState state, IShellActions actions)
    {
        var v = UiKit.VStack(12);
        v.Children.Add(UiKit.Title("Can't reach the AI"));
        v.Children.Add(UiKit.Body(state.AiUnavailableHint
            ?? "AccountaBall needs a local model. Install Ollama and pull a model, then retry."));
        v.Children.Add(UiKit.Primary("Download Ollama", (_, _) => actions.OpenOllamaDownload()));
        v.Children.Add(UiKit.Secondary("Retry", (_, _) => actions.RetryAi()));
        v.Children.Add(UiKit.Caption("e.g. `ollama run qwen2.5:7b`, or set AI_PROVIDER / OPENROUTER_API_KEY."));
        Content = UiKit.Card(v);
    }
}
