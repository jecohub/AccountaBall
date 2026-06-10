namespace AccountaBall.Core.Models;

/// The app's lifecycle phases. Faithful port of Swift `AppPhase`
/// (src/Sources/AccountaBall/Models/AppPhase.swift), including the FreeBall
/// passive-observation phases.
public enum AppPhase
{
    Idle,
    Welcome,
    Setup,
    Session,
    WhatsUp,
    Ambiguous,
    OffTask,
    Progress,
    Complete,
    AiUnavailable,
    // FreeBall — passive observation mode (separate engine).
    FreeBall,        // collapsed calm "observing" ball
    FreeBallLog,     // live session log: timer + End Session
    FreeBallRecap,   // post-session summary + breakdown
    FreeBallHistory, // past-sessions browser
}
