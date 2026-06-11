using AccountaBall.App.Shell;
using AccountaBall.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AccountaBall.App.Views;

/// Declare tasks + the pre-committed drift limit, then start watching. Port of the
/// macOS TaskSetupView (incl. the drift-limit stepper, default 3, range 1–10,
/// setup-only). The list rebuilds each Bind, so add/remove just re-render.
public sealed class TaskSetupView : UserControl, IPhaseView
{
    public void Bind(AppState state, IShellActions actions)
    {
        var v = UiKit.VStack(12);
        v.Children.Add(UiKit.Title("What are you working on?"));

        // Existing tasks with remove buttons.
        if (state.Tasks.Count > 0)
        {
            var list = UiKit.VStack(6);
            for (int i = 0; i < state.Tasks.Count; i++)
            {
                int idx = i;
                var row = UiKit.HStack(8);
                var label = UiKit.Body($"• {state.Tasks[i].Task}");
                label.HorizontalAlignment = HorizontalAlignment.Left;
                var remove = new Button { Content = "✕", Padding = new Thickness(8, 2, 8, 2) };
                remove.Click += (_, _) => actions.RemoveTask(idx);
                row.Children.Add(remove);
                row.Children.Add(label);
                list.Children.Add(row);
            }
            v.Children.Add(list);
        }

        // Add-task inputs.
        var taskBox = new TextBox { PlaceholderText = "Task (e.g. Finish the Q3 deck)" };
        var ctxBox = new TextBox { PlaceholderText = "Context (what counts as on-task)" };
        var addBtn = UiKit.Secondary("Add task", (_, _) =>
        {
            actions.AddTask(taskBox.Text?.Trim() ?? "", ctxBox.Text?.Trim() ?? "");
        });
        v.Children.Add(taskBox);
        v.Children.Add(ctxBox);
        v.Children.Add(addBtn);

        // Drift-limit stepper.
        var driftRow = UiKit.HStack(10);
        driftRow.Children.Add(UiKit.Body("Drift limit"));
        var drift = new NumberBox
        {
            Minimum = 1,
            Maximum = 10,
            Value = state.DriftLimit,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };
        drift.ValueChanged += (_, e) =>
        {
            if (!double.IsNaN(e.NewValue)) actions.SetDriftLimit((int)e.NewValue);
        };
        driftRow.Children.Add(drift);
        v.Children.Add(driftRow);
        v.Children.Add(UiKit.Caption("How many confirmed drifts before you've broken your commitment."));

        // Start (enabled once at least one task is fully filled in).
        bool canStart = state.Tasks.Exists(t => t.IsFilledIn);
        var start = UiKit.Primary("Start watching", (_, _) => actions.StartSession());
        start.IsEnabled = canStart;
        v.Children.Add(start);

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = UiKit.Card(v),
        };
    }
}
