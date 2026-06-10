namespace AccountaBall.Core.Util;

/// App-wide constants shared across the engine and utilities so values that must
/// stay in lockstep can't drift apart. Faithful port of Swift `AppConstants`
/// (src/Sources/AccountaBall/Util/AppConstants.swift). Seconds are whole numbers
/// (the macOS values are all integral), expressed as int.
public static class AppConstants
{
    /// The capture/classify cycle length (seconds). The capture loop fires on this
    /// interval; each on-task cycle credits this many seconds to the task.
    public const int CycleSeconds = 3;

    /// Characters of the full-screen OCR kept as the lighter peripheral signal
    /// alongside the focused window's full text.
    public const int PeripheralScreenChars = 500;

    /// Grace breather granted by the off-task "Continue anyway" escape hatch (seconds).
    public const int ContinueAnywayGraceSeconds = 120;

    /// Length of the opt-in "timed break" offered on a confirmed drift (seconds).
    public const int BreakSeconds = 5 * 60;

    /// A drift is only confirmed after staying on the SAME off-task screen this long.
    public const int DriftConfirmSeconds = 15;

    /// FreeBall: Jaccard word-set similarity at/above which two consecutive OCR
    /// reads are treated as the same screen.
    public const double FreeBallDedupThreshold = 0.85;

    /// FreeBall: max characters of deduped transcript fed to the summarizer.
    public const int FreeBallMaxTranscriptChars = 28000;

    /// FreeBall: a session with less captured text than this is "trivial".
    public const int FreeBallMinCharsToSummarize = 40;

    /// FreeBall: how many recent past-session recaps to feed as cross-session context.
    public const int FreeBallPastRecapCap = 10;

    /// FreeBall: per-request timeout for the End-Session summarize call (seconds).
    public const int FreeBallSummarizeTimeout = 120;
}
