using System;
using System.Collections.Generic;

namespace AccountaBall.Core.Models;

/// A labeled slice of session time (the categorized breakdown). Port of Swift
/// `CategorySpan`.
public sealed record CategorySpan(string Label, int Minutes);

/// One FreeBall session. Port of the SwiftData `FreeBallSession` @Model (POCO; the
/// real persistence is EF Core/SQLite in M3).
public sealed class FreeBallSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int CycleCount { get; set; }
    // Distilled recap (kept long-term; feeds cross-session learning).
    public string Narrative { get; set; } = string.Empty;
    public List<CategorySpan> Categories { get; set; } = new();
    public string Insight { get; set; } = string.Empty;
    // Context extraction (grounded in the transcript), kept long-term.
    public List<string> WorkingOn { get; set; } = new();
    public List<string> People { get; set; } = new();
    public List<string> CodeContext { get; set; } = new();
    public List<string> OpenThreads { get; set; } = new();
    /// True when End Session couldn't reach the AI; raw captures kept for a later pass.
    public bool RecapPending { get; set; }
    public List<FreeBallCapture> Captures { get; } = new();

    public FreeBallSession(DateTimeOffset startedAt) => StartedAt = startedAt;
}

/// One deduped on-screen block. Port of the SwiftData `FreeBallCapture` @Model.
public sealed class FreeBallCapture
{
    public DateTimeOffset FirstSeenAt { get; set; }   // when this screen first appeared
    public DateTimeOffset LastSeenAt { get; set; }    // extended while the screen stays unchanged
    public string Text { get; set; }                  // full OCR text of the screen

    public FreeBallCapture(DateTimeOffset firstSeenAt, DateTimeOffset lastSeenAt, string text)
    {
        FirstSeenAt = firstSeenAt;
        LastSeenAt = lastSeenAt;
        Text = text;
    }

    /// How long this screen was up, in seconds.
    public double Seconds => (LastSeenAt - FirstSeenAt).TotalSeconds;
}
