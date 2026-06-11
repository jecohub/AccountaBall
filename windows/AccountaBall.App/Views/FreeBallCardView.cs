using AccountaBall.App.Shell;
using AccountaBall.Core.Models;
using Microsoft.UI.Xaml.Controls;

namespace AccountaBall.App.Views;

/// FreeBall card for the non-ball phases (Log / Recap / History). Port of
/// FreeBallView + FreeBallHistoryView. Passive mode does zero AI mid-session, so
/// the Log view just confirms it's observing; the Recap shows the end-of-session
/// summary state; History browses past sessions.
///
/// NOTE: FreeBall recap/history data types land with M2.6 on the Core side; this
/// card renders the available AppState bridges (summarizing / viewing-history) and
/// the lifecycle actions. Flesh out the rich recap once the recap type is wired.
public sealed class FreeBallCardView : UserControl, IPhaseView
{
    public void Bind(AppState state, IShellActions actions)
    {
        var v = UiKit.VStack(12);

        switch (state.AppPhase)
        {
            case AppPhase.FreeBallLog:
                v.Children.Add(UiKit.Title("Observing"));
                v.Children.Add(UiKit.Body("Quietly recording where your time goes — no judgement, no AI until you stop."));
                v.Children.Add(UiKit.Primary("End & summarize", (_, _) => actions.EndFreeBall()));
                v.Children.Add(UiKit.Secondary("Past sessions", (_, _) => actions.ViewFreeBallHistory()));
                break;

            case AppPhase.FreeBallRecap:
                v.Children.Add(UiKit.Title("Where your time went"));
                v.Children.Add(UiKit.Body(state.FreeBallSummarizing
                    ? "Summarizing this session…"
                    : "Session summary ready."));
                v.Children.Add(UiKit.Secondary("Past sessions", (_, _) => actions.ViewFreeBallHistory()));
                v.Children.Add(UiKit.Primary("Done", (_, _) => actions.CloseFreeBallHistory()));
                break;

            case AppPhase.FreeBallHistory:
                v.Children.Add(UiKit.Title("Past sessions"));
                v.Children.Add(UiKit.Body("Browse and export earlier FreeBall summaries."));
                v.Children.Add(UiKit.Primary("Close", (_, _) => actions.CloseFreeBallHistory()));
                break;
        }

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = UiKit.Card(v),
        };
    }
}
