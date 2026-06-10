namespace AccountaBall.Core.Models;

/// The floating ball's expression. Port of Swift `BallState`, plus the calm
/// `Observing` face added by FreeBall (2026-06-09-freeball-design.md §1).
public enum BallState
{
    Idle,
    OnTask,
    OffTask,
    Done,
    Observing,
}
