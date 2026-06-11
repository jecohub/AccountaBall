using AccountaBall.Core.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace AccountaBall.App.Views;

/// The floating basketball with a per-<see cref="BallState"/> face. Port of the
/// macOS BallView / BasketballView / SessionBallView expressions: sleepy when
/// Idle, happy On-Task, concerned Off-Task, beaming when Done, calm while
/// Observing (FreeBall).
public sealed partial class BallView : UserControl
{
    public BallView()
    {
        this.InitializeComponent();
        SetState(BallState.Idle);
    }

    public void SetState(BallState state)
    {
        switch (state)
        {
            case BallState.Idle:
                SetEyes(sleepy: true);
                Mouth.Data = Curve(38, 64, 50, 65, 62, 64);   // flat, drowsy
                break;
            case BallState.OnTask:
                SetEyes(sleepy: false);
                Mouth.Data = Curve(36, 60, 50, 74, 64, 60);   // smile
                break;
            case BallState.OffTask:
                SetEyes(sleepy: false);
                Mouth.Data = Curve(36, 68, 50, 58, 64, 68);   // frown
                break;
            case BallState.Done:
                SetEyes(sleepy: false);
                Mouth.Data = Curve(34, 58, 50, 80, 66, 58);   // big grin
                break;
            case BallState.Observing:
                SetEyes(sleepy: false);
                Mouth.Data = Curve(38, 63, 50, 70, 62, 63);   // gentle, attentive
                break;
        }
    }

    /// Reshape both eyes: round and open, or thin sleepy slits (kept vertically
    /// centered around the same line).
    private void SetEyes(bool sleepy)
    {
        double h = sleepy ? 3 : 9;
        double top = sleepy ? 44 : 40;
        LeftEye.Height = h;
        RightEye.Height = h;
        LeftEye.Margin = new Microsoft.UI.Xaml.Thickness(32, top, 0, 0);
        RightEye.Margin = new Microsoft.UI.Xaml.Thickness(59, top, 0, 0);
    }

    /// A quadratic-bezier mouth in the ball's 100x100 face space.
    private static PathGeometry Curve(double x1, double y1, double cx, double cy, double x2, double y2)
    {
        var figure = new PathFigure { StartPoint = new Point(x1, y1) };
        figure.Segments.Add(new QuadraticBezierSegment { Point1 = new Point(cx, cy), Point2 = new Point(x2, y2) });
        var geo = new PathGeometry();
        geo.Figures.Add(figure);
        return geo;
    }
}
