using AccountaBall.App.Shell;
using AccountaBall.Core.Models;
using Microsoft.UI.Xaml.Controls;

namespace AccountaBall.App.Views;

/// End-of-session recap + transparency log. Port of CompletionView. Renders from
/// the persisted <see cref="SessionRecap"/> when present (faithful even if the AI
/// commentary failed), including the ◐/●/○ check log and the broken-commitment
/// line.
public sealed class CompletionView : UserControl, IPhaseView
{
    public void Bind(AppState state, IShellActions actions)
    {
        var v = UiKit.VStack(10);
        v.Children.Add(UiKit.Title("Session complete"));

        if (state.LastSessionDuration is { } secs)
            v.Children.Add(UiKit.Body($"Total focus: {(int)(secs / 60)} min"));

        var recap = state.SessionRecap;
        if (recap is not null)
        {
            // Per-task commentary.
            foreach (var pt in recap.PerTask)
            {
                v.Children.Add(UiKit.Body($"• {pt.TaskTitle}: {pt.Comment}"));
                if (!string.IsNullOrWhiteSpace(pt.Suggestion))
                    v.Children.Add(UiKit.Caption($"  ↳ {pt.Suggestion}"));
            }

            // Drift summary / broken-commitment line.
            var driftLine = $"Drifts: {recap.DriftCount} of {recap.DriftLimit}"
                + (recap.CommitmentBroken ? " — commitment broken" : "");
            v.Children.Add(UiKit.Body(driftLine));

            // Transparency log.
            if (recap.Checks.Count > 0)
            {
                v.Children.Add(UiKit.Caption("Check log"));
                foreach (var c in recap.Checks)
                    v.Children.Add(UiKit.Caption($"{Glyph(c.Kind)} {c.Activity} — {c.Note}"));
            }
        }
        else
        {
            // No AI recap (e.g. provider down): still close out honestly.
            foreach (var t in state.Tasks)
                v.Children.Add(UiKit.Body($"{(t.IsComplete ? "✓" : "•")} {t.Task}"));
        }

        v.Children.Add(UiKit.Primary("Done", (_, _) => actions.DismissCompletion()));

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = UiKit.Card(v),
        };
    }

    /// ●  confirmed off-task drift · ◐ ambiguous ask · ○ honest auto-return.
    private static string Glyph(string kind) => kind switch
    {
        "offtask" => "●",
        "ambiguous" => "◐",
        "auto-return" => "○",
        _ => "·",
    };
}
