using System;
using System.Collections.Generic;

namespace AccountaBall.Core.Models;

// Durable history entities. Port of the Swift SwiftData @Model classes
// (src/Sources/AccountaBall/Models/Persistence.swift). Kept as reference-type
// classes so identity/relationship semantics match SwiftData (e.g. the engine
// removes an Allowance by reference). The real persistence (EF Core/SQLite) is
// the Platform layer (M3); these POCOs + IStore are what the engine talks to.

/// One focus session. `Entries` and `Justifications` are owned (cascade) children.
public sealed class WorkSession
{
    public Guid Id { get; set; } = Guid.NewGuid();   // stable id for cross-session links (captures/contributions)
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public List<TimelineEntry> Entries { get; } = new();
    public List<JustificationEvent> Justifications { get; } = new();
    public List<string> TaskTitles { get; set; } = new();   // snapshot of what was declared

    public WorkSession(DateTimeOffset startedAt) => StartedAt = startedAt;
}

/// One per capture cycle. `TaskIndex == null` means off-task.
public sealed class TimelineEntry
{
    public DateTimeOffset At { get; set; }
    public int? TaskIndex { get; set; }
    public string Label { get; set; }

    public TimelineEntry(DateTimeOffset at, int? taskIndex, string label)
    {
        At = at;
        TaskIndex = taskIndex;
        Label = label;
    }
}

/// The unified check log row. `Kind` is "ambiguous" | "offtask" | "auto-return".
public sealed class JustificationEvent
{
    public DateTimeOffset At { get; set; }
    public string Excuse { get; set; }
    public bool Justified { get; set; }
    public int? InferredTaskIndex { get; set; }
    public string Activity { get; set; }
    public string Rule { get; set; }
    public string Kind { get; set; }

    public JustificationEvent(DateTimeOffset at, string excuse, bool justified, int? inferredTaskIndex,
                              string activity, string rule = "", string kind = "offtask")
    {
        At = at;
        Excuse = excuse;
        Justified = justified;
        InferredTaskIndex = inferredTaskIndex;
        Activity = activity;
        Rule = rule;
        Kind = kind;
    }
}

/// Cross-session task memory, keyed by normalized title.
public sealed class KnowledgeTask
{
    public string NormalizedTitle { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<string> OriginalTitles { get; set; } = new();
    public DateTimeOffset LastCompletedAt { get; set; }
    public int TimesCompleted { get; set; }
    public List<Allowance> Allowances { get; } = new();
    public List<TaskCompletion> Completions { get; } = new();

    public KnowledgeTask(string normalizedTitle, DateTimeOffset lastCompletedAt)
    {
        NormalizedTitle = normalizedTitle;
        LastCompletedAt = lastCompletedAt;
    }
}

/// "This activity counts as on-task" rule. `NeedsConfirmation` true when revived
/// in a new session and awaiting one-time user confirmation.
public sealed class Allowance
{
    public string Rule { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool NeedsConfirmation { get; set; }

    public Allowance(string rule, DateTimeOffset createdAt, bool needsConfirmation = false)
    {
        Rule = rule;
        CreatedAt = createdAt;
        NeedsConfirmation = needsConfirmation;
    }
}

/// One finished run of a task.
public sealed class TaskCompletion
{
    public DateTimeOffset CompletedAt { get; set; }
    public double Duration { get; set; }
    public string Summary { get; set; }
    public List<string> Steps { get; set; }
    public int OffTaskCount { get; set; }

    public TaskCompletion(DateTimeOffset completedAt, double duration, string summary,
                          List<string> steps, int offTaskCount)
    {
        CompletedAt = completedAt;
        Duration = duration;
        Summary = summary;
        Steps = steps;
        OffTaskCount = offTaskCount;
    }
}
