using System;

namespace AccountaBall.Core.Models;

/// Join between a session and a project/thread: what this session advanced.
/// UUID soft-refs (not EF relationships) so it can span WorkSession/FreeBallSession.
public sealed class Contribution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset At { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? ThreadId { get; set; }
    public Guid SessionId { get; set; }
    public string SessionKind { get; set; }   // "task" | "free"
    public int Minutes { get; set; }
    public string Summary { get; set; }

    public Contribution(DateTimeOffset at, Guid projectId, Guid? threadId, Guid sessionId,
                        string sessionKind, int minutes, string summary)
    {
        At = at; ProjectId = projectId; ThreadId = threadId; SessionId = sessionId;
        SessionKind = sessionKind; Minutes = minutes; Summary = summary;
    }
}

/// One model-proposal + user-decision from session-end entity resolution.
public sealed class Judgment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset At { get; set; }
    public DateTimeOffset CaptureRangeStart { get; set; }
    public DateTimeOffset CaptureRangeEnd { get; set; }
    public string ModelProposal { get; set; }   // JSON
    public string UserDecision { get; set; }     // "accepted" | "rejected" | "edited"

    public Judgment(DateTimeOffset at, DateTimeOffset captureRangeStart, DateTimeOffset captureRangeEnd,
                    string modelProposal, string userDecision)
    {
        At = at; CaptureRangeStart = captureRangeStart; CaptureRangeEnd = captureRangeEnd;
        ModelProposal = modelProposal; UserDecision = userDecision;
    }
}
