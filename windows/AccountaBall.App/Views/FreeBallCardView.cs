using System;
using System.Collections.Generic;
using AccountaBall.App.Shell;
using AccountaBall.Core.Models;
using Microsoft.UI.Xaml.Controls;

namespace AccountaBall.App.Views;

/// FreeBall card for the non-ball phases (Log / Recap / History). Port of
/// FreeBallView + FreeBallHistoryView. Passive mode does zero AI mid-session, so
/// the Log view just confirms it's observing; the Recap renders the end-of-session
/// summary (narrative + categorized time + insight + extracted context); History
/// browses past sessions via <see cref="IShellActions.FreeBallHistory"/>.
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
                if (state.FreeBallSummarizing)
                {
                    v.Children.Add(UiKit.Body("Summarizing this session…"));
                }
                else if (state.FreeBallRecap is { } recap)
                {
                    RenderRecap(v, recap);
                }
                else
                {
                    v.Children.Add(UiKit.Body("No summary available."));
                }
                v.Children.Add(UiKit.Secondary("Past sessions", (_, _) => actions.ViewFreeBallHistory()));
                v.Children.Add(UiKit.Primary("Done", (_, _) => actions.CloseFreeBallHistory()));
                break;

            case AppPhase.FreeBallHistory:
                v.Children.Add(UiKit.Title("History"));
                var history = actions.History();
                if (history.Count == 0)
                {
                    v.Children.Add(UiKit.Body("No past sessions yet."));
                }
                else
                {
                    foreach (var h in history)
                    {
                        v.Children.Add(UiKit.Body($"[{h.Type}] · {h.Date.LocalDateTime:MMM d, h:mm tt}"));
                        v.Children.Add(UiKit.Body(h.Summary));
                    }
                }
                v.Children.Add(UiKit.Primary("Close", (_, _) => actions.CloseFreeBallHistory()));
                break;
        }

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = UiKit.Card(v),
        };
    }

    /// Render one completed FreeBall summary: narrative, the categorized time
    /// breakdown, the cross-session insight, and any extracted context lists.
    private static void RenderRecap(StackPanel v, FreeBallRecap r)
    {
        if (r.RecapPending)
        {
            v.Children.Add(UiKit.Body("Couldn't summarize — the AI was unreachable. Your capture was saved."));
            return;
        }

        if (!string.IsNullOrWhiteSpace(r.Narrative)) v.Children.Add(UiKit.Body(r.Narrative));

        foreach (var c in r.Categories)
            v.Children.Add(UiKit.Body($"• {c.Label} — {c.Minutes}m"));

        if (!string.IsNullOrWhiteSpace(r.Insight)) v.Children.Add(UiKit.Body($"Insight: {r.Insight}"));

        AddListSection(v, "Working on", r.WorkingOn);
        AddListSection(v, "People", r.People);
        AddListSection(v, "Code", r.CodeContext);
        AddListSection(v, "Open threads", r.OpenThreads);
    }

    private static void AddListSection(StackPanel v, string heading, IReadOnlyList<string> items)
    {
        if (items.Count == 0) return;
        v.Children.Add(UiKit.Body($"{heading}:"));
        foreach (var item in items) v.Children.Add(UiKit.Body($"  – {item}"));
    }
}
