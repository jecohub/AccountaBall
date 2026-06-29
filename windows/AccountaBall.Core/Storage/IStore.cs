using System;
using System.Collections.Generic;
using System.Linq;
using AccountaBall.Core.Models;

namespace AccountaBall.Core.Storage;

/// Durable-history store, the Windows analog of the macOS SwiftData `ModelContext`.
/// The engine treats it as optional (null = a no-op stub path, mirroring a nil
/// `modelContext`). KnowledgeTasks are queried via LINQ over `KnowledgeTasks`;
/// child entities (entries, justifications, allowances) live on their parent's
/// relationship lists, so they need no explicit insert here. The Platform layer
/// (M3) backs this with EF Core/SQLite.
public interface IStore
{
    void AddSession(WorkSession session);
    void AddKnowledgeTask(KnowledgeTask knowledgeTask);
    IReadOnlyList<KnowledgeTask> KnowledgeTasks { get; }
    void DeleteAllowance(Allowance allowance);
    void AddFreeBallSession(FreeBallSession session);
    IReadOnlyList<FreeBallSession> FreeBallSessions { get; }
    void AddCapture(Capture capture);
    IReadOnlyList<Capture> Captures { get; }
    Capture? LatestCaptureForSession(Guid sessionId);
    void Save();
}

/// Process-memory store for tests and the engine's pure logic.
public sealed class InMemoryStore : IStore
{
    private readonly List<WorkSession> _sessions = new();
    private readonly List<KnowledgeTask> _knowledgeTasks = new();
    private readonly List<FreeBallSession> _freeBallSessions = new();

    public void AddSession(WorkSession session) => _sessions.Add(session);
    public void AddKnowledgeTask(KnowledgeTask knowledgeTask) => _knowledgeTasks.Add(knowledgeTask);
    public IReadOnlyList<KnowledgeTask> KnowledgeTasks => _knowledgeTasks;

    public void DeleteAllowance(Allowance allowance)
    {
        foreach (var kt in _knowledgeTasks) kt.Allowances.Remove(allowance);
    }

    public void AddFreeBallSession(FreeBallSession session) => _freeBallSessions.Add(session);
    public IReadOnlyList<FreeBallSession> FreeBallSessions => _freeBallSessions;

    private readonly List<Capture> _captures = new();
    public void AddCapture(Capture capture) => _captures.Add(capture);
    public IReadOnlyList<Capture> Captures => _captures;
    public Capture? LatestCaptureForSession(Guid sessionId) =>
        _captures.Where(c => c.SessionId == sessionId)
                 .OrderByDescending(c => c.LastSeenAt).FirstOrDefault();

    public void Save() { /* in-memory: relationship lists are the source of truth */ }

    /// Test/inspection helper (not on the interface).
    public IReadOnlyList<WorkSession> Sessions => _sessions;
}
