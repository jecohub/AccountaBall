using System;
using System.Collections.Generic;
using System.IO;
using AccountaBall.Core.Models;
using AccountaBall.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace AccountaBall.Platform.Storage;

/// <see cref="IStore"/> backed by EF Core + SQLite — the durable history the
/// engine reads its tamper-proof <c>DriftCount</c> from. DB lives at
/// <c>%LOCALAPPDATA%\AccountaBall\accountaball.db</c>. Schema is created on first
/// run via <c>EnsureCreated</c> (v1 ships without EF migration tooling; switch to
/// migrations when the schema starts evolving).
///
/// The context is tracking: entities returned from <see cref="KnowledgeTasks"/>
/// are live, so the engine can mutate a returned KnowledgeTask's allowances and
/// call <see cref="Save"/> to persist — matching the SwiftData semantics the
/// engine was written against.
public sealed class SqliteStore : IStore, IDisposable
{
    private readonly AccountaBallDbContext _db;

    public SqliteStore(AccountaBallDbContext db)
    {
        _db = db;
        _db.Database.EnsureCreated();
    }

    /// Default location: %LOCALAPPDATA%\AccountaBall\accountaball.db.
    public static SqliteStore CreateDefault()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AccountaBall");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "accountaball.db");

        var options = new DbContextOptionsBuilder<AccountaBallDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new SqliteStore(new AccountaBallDbContext(options));
    }

    public void AddSession(WorkSession session) => _db.Sessions.Add(session);

    /// Past declared sessions (with their timeline + check-log), for the history
    /// browser. Not on the Core <see cref="IStore"/> — App-layer read only.
    public IReadOnlyList<WorkSession> Sessions =>
        _db.Sessions
            .Include(s => s.Entries)
            .Include(s => s.Justifications)
            .ToList();

    public void AddKnowledgeTask(KnowledgeTask knowledgeTask) => _db.KnowledgeTasks.Add(knowledgeTask);

    public IReadOnlyList<KnowledgeTask> KnowledgeTasks =>
        _db.KnowledgeTasks
            .Include(k => k.Allowances)
            .Include(k => k.Completions)
            .ToList();

    public void DeleteAllowance(Allowance allowance) => _db.Remove(allowance);

    public void AddFreeBallSession(FreeBallSession session) => _db.FreeBallSessions.Add(session);

    public IReadOnlyList<FreeBallSession> FreeBallSessions =>
        _db.FreeBallSessions
            .Include(s => s.Captures)
            .ToList();

    public void Save() => _db.SaveChanges();

    public void Dispose() => _db.Dispose();
}
