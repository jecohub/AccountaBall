using System;
using System.Linq;
using AccountaBall.App.Shell;
using AccountaBall.Core.Models;
using Microsoft.UI.Xaml.Controls;

namespace AccountaBall.App.Views;

/// "What are you up to?" check-in. Port of the macOS WhatsUp prompt. Minimal:
/// confirm you're working or jump back to setup.
public sealed class WhatsUpView : UserControl, IPhaseView
{
    public void Bind(AppState state, IShellActions actions)
    {
        var v = UiKit.VStack(12);
        v.Children.Add(UiKit.Title("Still on it?"));
        v.Children.Add(UiKit.Body("Tap to keep going, or adjust your tasks."));
        v.Children.Add(UiKit.Primary("Keep watching", (_, _) => actions.ResumeWatching(false)));
        v.Children.Add(UiKit.Secondary("Edit tasks", (_, _) => actions.OpenSetup()));
        Content = UiKit.Card(v);
    }
}

/// Ask-once card for ambiguous, work-shaped activity. Port of AmbiguousAskView.
/// "Yes, related" takes an optional reason and creates an allowance; "No" logs a
/// confirmed drift.
public sealed class AmbiguousView : UserControl, IPhaseView
{
    public void Bind(AppState state, IShellActions actions)
    {
        var v = UiKit.VStack(12);
        v.Children.Add(UiKit.Title("Is this related to your task?"));
        v.Children.Add(UiKit.Body("I can't tell if this screen is part of your work."));
        var reason = new TextBox { PlaceholderText = "Why it's related (optional)" };
        v.Children.Add(reason);
        v.Children.Add(UiKit.Primary("Yes, it's related", (_, _) => actions.AcceptAmbiguous(reason.Text?.Trim() ?? "")));
        v.Children.Add(UiKit.Secondary("No, I drifted", (_, _) => actions.RejectAmbiguous()));
        Content = UiKit.Card(v);
    }
}

/// Confirmed-drift card. Port of OffTaskView. Offers a timed break, a back-to-work
/// resume, or "it's actually related" (grace for the current activity).
public sealed class OffTaskView : UserControl, IPhaseView
{
    public void Bind(AppState state, IShellActions actions)
    {
        var v = UiKit.VStack(12);
        v.Children.Add(UiKit.Title("You've drifted off task"));
        var task = state.ActiveTasks.FirstOrDefault()?.Task;
        if (!string.IsNullOrEmpty(task))
            v.Children.Add(UiKit.Body($"Your task: {task}"));
        v.Children.Add(UiKit.Primary("Back to work", (_, _) => actions.ResumeWatching(false)));
        v.Children.Add(UiKit.Secondary("Take a 5-min break", (_, _) => actions.TakeBreak()));
        v.Children.Add(UiKit.Secondary("It's actually related", (_, _) => actions.ResumeWatching(true)));
        Content = UiKit.Card(v);
    }
}

/// Live progress card. Port of ProgressView — elapsed time + per-task time-on-task.
public sealed class ProgressView : UserControl, IPhaseView
{
    public void Bind(AppState state, IShellActions actions)
    {
        var v = UiKit.VStack(10);
        v.Children.Add(UiKit.Title("Progress"));
        if (state.SessionStartTime is { } start)
        {
            var mins = (int)((DateTimeOffset.UtcNow - start).TotalMinutes);
            v.Children.Add(UiKit.Body($"Session: {mins} min"));
        }
        foreach (var t in state.Tasks)
        {
            var mark = t.IsComplete ? "✓" : "•";
            v.Children.Add(UiKit.Body($"{mark} {t.Task} — {(int)(t.TimeOnTask / 60)}m"));
        }
        v.Children.Add(UiKit.Secondary("Keep watching", (_, _) => actions.ResumeWatching(false)));
        Content = UiKit.Card(v);
    }
}
