using System;
using System.Collections.Generic;

namespace AccountaBall.Core.Models;

/// One deduped block of the live session, fed to the summarizer. Port of Swift
/// `FreeBallTranscriptEntry`.
public sealed record FreeBallTranscriptEntry(string Text, double Seconds);

/// A prior session's distilled recap, fed as cross-session context. Port of Swift
/// `FreeBallPastRecap`.
public sealed record FreeBallPastRecap(
    string Narrative, IReadOnlyList<CategorySpan> Categories, string Insight, IReadOnlyList<string> OpenThreads);

/// The AI's structured output for one ended session. Port of Swift `FreeBallSummary`.
public sealed record FreeBallSummary(
    string Narrative,
    IReadOnlyList<CategorySpan> Categories,
    string Insight,
    IReadOnlyList<string> WorkingOn,
    IReadOnlyList<string> People,
    IReadOnlyList<string> CodeContext,
    IReadOnlyList<string> OpenThreads);

/// UI-facing recap published to AppState. Port of Swift `FreeBallRecap`.
public sealed record FreeBallRecap(
    DateTimeOffset Date,
    double Duration,
    string Narrative,
    IReadOnlyList<CategorySpan> Categories,
    string Insight,
    IReadOnlyList<string> WorkingOn,
    IReadOnlyList<string> People,
    IReadOnlyList<string> CodeContext,
    IReadOnlyList<string> OpenThreads,
    bool RecapPending)
{
    /// Build a recap from a stored session (history browser + re-open).
    public static FreeBallRecap FromSession(FreeBallSession s)
    {
        var end = s.EndedAt ?? s.StartedAt;
        return new FreeBallRecap(
            end, (end - s.StartedAt).TotalSeconds, s.Narrative, s.Categories, s.Insight,
            s.WorkingOn, s.People, s.CodeContext, s.OpenThreads, s.RecapPending);
    }
}
