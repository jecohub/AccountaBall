using AccountaBall.App.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace AccountaBall.App.Shell;

/// Makes a borderless WinUI element drag the whole <see cref="FloatingPanel"/> window —
/// the Windows analog of dragging the macOS NSPanel by its body. Attached to the ball
/// and the card host so the user can reposition the floating widget.
///
/// Pointer capture is deferred until the cursor moves past a small threshold, so a plain
/// click never captures: the ball's Tapped and the cards' button Clicks still fire. Once
/// dragging, the window follows the absolute cursor position (GetCursorPos), which avoids
/// the feedback loop you'd get from a moving-frame relative delta.
public static class WindowDrag
{
    private const double Threshold = 4.0; // DIPs of travel before a press becomes a drag

    public static void Attach(UIElement element, FloatingPanel panel)
    {
        var state = new DragState();

        element.PointerPressed += (s, e) =>
        {
            // Don't hijack text selection inside a TextBox.
            if (e.OriginalSource is TextBox) return;

            var p = e.GetCurrentPoint(element);
            if (!p.Properties.IsLeftButtonPressed) return;

            NativeWindow.GetCursorPos(out var cursor);
            state.Pressed = true;
            state.Dragging = false;
            state.StartCursorX = cursor.X;
            state.StartCursorY = cursor.Y;
            state.OffsetX = cursor.X - panel.Position.X;
            state.OffsetY = cursor.Y - panel.Position.Y;
        };

        element.PointerMoved += (s, e) =>
        {
            if (!state.Pressed) return;
            NativeWindow.GetCursorPos(out var cursor);

            if (!state.Dragging)
            {
                if (System.Math.Abs(cursor.X - state.StartCursorX) < Threshold &&
                    System.Math.Abs(cursor.Y - state.StartCursorY) < Threshold)
                    return;
                // Crossed the threshold — this is a drag, not a click. Capture now so we
                // keep tracking even if the cursor leaves the element.
                state.Dragging = element.CapturePointer(e.Pointer);
                if (!state.Dragging) return;
            }

            panel.MoveTo(cursor.X - state.OffsetX, cursor.Y - state.OffsetY);
        };

        void End(PointerRoutedEventArgs e)
        {
            if (state.Dragging) element.ReleasePointerCapture(e.Pointer);
            state.Pressed = false;
            state.Dragging = false;
        }

        element.PointerReleased += (s, e) => End(e);
        element.PointerCaptureLost += (s, e) => { state.Pressed = false; state.Dragging = false; };
    }

    private sealed class DragState
    {
        public bool Pressed;
        public bool Dragging;
        public int StartCursorX, StartCursorY;
        public int OffsetX, OffsetY;
    }
}
