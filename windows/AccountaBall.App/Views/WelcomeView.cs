using AccountaBall.App.Shell;
using AccountaBall.Core.Models;
using Microsoft.UI.Xaml.Controls;

namespace AccountaBall.App.Views;

/// First-run / idle entry card. Port of the macOS WelcomeView. Offers the
/// declared-task flow or the passive FreeBall mode.
public sealed class WelcomeView : UserControl, IPhaseView
{
    public void Bind(AppState state, IShellActions actions)
    {
        var v = UiKit.VStack(12);
        v.Children.Add(UiKit.Title("AccountaBall"));
        v.Children.Add(UiKit.Body("Tell me what you're working on and I'll watch your screen to keep you honest."));
        v.Children.Add(UiKit.Primary("Set up a session", (_, _) => actions.OpenSetup()));
        v.Children.Add(UiKit.Secondary("Just observe (FreeBall)", (_, _) => actions.StartFreeBall()));
        if (!string.IsNullOrEmpty(state.SetupHint))
            v.Children.Add(UiKit.Caption(state.SetupHint!));
        Content = UiKit.Card(v);
    }
}
