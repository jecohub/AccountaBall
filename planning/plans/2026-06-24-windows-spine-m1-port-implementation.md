# Windows Context-Spine M1 Port — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: superpowers:executing-plans (or subagent-driven-development) to implement task-by-task.

**Goal:** Bring the Windows port to parity with the macOS Context Spine M1 — task mode
persists full OCR into a `Capture`, de-duped; plus the `Project`/`Thread`/
`Contribution`/`Judgment` spine entities. Faithful port of the macOS M1
(`planning/plans/2026-06-24-accumulating-context-spine-m1-implementation.md`,
shipped on `src` master `332ce78`).

**Architecture:** Mirrors macOS but adapted to the Windows split:
- **`AccountaBall.Core`** (net8.0, **builds + `dotnet test`s on macOS**) — POCO models,
  the `IStore` abstraction + `InMemoryStore`, and the engine logic. Persistence is
  via `IStore` (not EF directly). Tests are xUnit.
- **`AccountaBall.Platform`** (net8.0-windows, **Windows-only**) — the EF/SQLite
  `IStore` impl (`SqliteStore`) + `AccountaBallDbContext`.
- **`AccountaBall.App`** (net8.0-windows, **Windows-only**) — `AppController` drives
  the capture loop.

**Capture shape:** mirror macOS exactly — a standalone `Capture` entity keyed by a
`SessionId` Guid soft-reference (NOT an EF child relationship), so it can later span
both `WorkSession` and `FreeBallSession` (FreeBall convergence is deferred, same as
mac). Dedup reads the latest capture for the session via a new `IStore` query.

**Platform note (important):** Tasks W1–W5 touch only Core + Core.Tests and are
fully verifiable here with `dotnet test windows/AccountaBall.Core.Tests`. Adding
methods to `IStore` means the Platform `SqliteStore` won't compile until W6 — that's
expected; **the Platform/App projects are left non-compiling until W6**, which is
written here but **compiled/verified by the user on Windows**.

**Repo/branch:** outer `AccountaBall` repo, branch `windows-port-m3-m4` (current).
The Windows code is committed directly here (NOT a separate worktree — it's the outer
repo, and `windows/` is tracked here).

---

## Setup

```bash
cd /Users/jericodelacruz/Desktop/AccountaBall
dotnet test windows/AccountaBall.Core.Tests   # baseline: expect 94/95 (1 known CRLF AiPrompts failure)
```
Record the baseline pass count. All new Core tests must keep that green (the one
pre-existing `AiPromptsTests` CRLF failure stays as-is per repo owner).

xUnit idiom (read `windows/AccountaBall.Core.Tests/FreeBallEngineTests.cs` +
`EngineTestSupport.cs`): `[Fact]`, `Assert.*`; build an engine via
`EngineTest.MakeWithSession(...)` / `MakeWithClock(...)`; `InMemoryStore` is the
test store; `MutableClock` drives `engine.Now`.

---

### Task W1: `Capture` model + `IStore` capture methods + InMemoryStore + test

**Files:**
- Create: `windows/AccountaBall.Core/Models/Capture.cs`
- Modify: `windows/AccountaBall.Core/Storage/IStore.cs` (interface + `InMemoryStore`)
- Create: `windows/AccountaBall.Core.Tests/SpineSchemaTests.cs`

**Step 1 — failing test.** Create `SpineSchemaTests.cs`:
```csharp
using System;
using System.Linq;
using AccountaBall.Core.Models;
using AccountaBall.Core.Storage;
using Xunit;

namespace AccountaBall.Core.Tests;

public class SpineSchemaTests
{
    [Fact]
    public void Capture_RoundTrips_AndLatestForSessionQuery()
    {
        var store = new InMemoryStore();
        var sid = Guid.NewGuid();
        var t0 = DateTimeOffset.UnixEpoch;
        store.AddCapture(new Capture(t0, t0, "screen A", "task", "Notepad", 0, sid));
        store.AddCapture(new Capture(t0.AddSeconds(5), t0.AddSeconds(5), "screen B", "task", null, 0, sid));
        store.AddCapture(new Capture(t0, t0, "other session", "task", null, null, Guid.NewGuid()));
        store.Save();

        var latest = store.LatestCaptureForSession(sid);
        Assert.NotNull(latest);
        Assert.Equal("screen B", latest!.Text);              // most-recent by LastSeenAt
        Assert.Equal("task", latest.Mode);
        Assert.Equal(2, store.Captures.Count(c => c.SessionId == sid));
    }
}
```

**Step 2 — run, expect FAIL** (`Capture` / `AddCapture` / `LatestCaptureForSession` don't exist):
`dotnet test windows/AccountaBall.Core.Tests --filter SpineSchemaTests`

**Step 3 — implement.** Create `Capture.cs`:
```csharp
using System;

namespace AccountaBall.Core.Models;

/// One screen read, shared by both modes (task mode no longer discards OCR).
/// Soft-referenced to its owning session by <see cref="SessionId"/> (NOT an EF
/// relationship) so a Capture can belong to either a WorkSession or a
/// FreeBallSession — a typed relationship can't span two entity types.
public sealed class Capture
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string Text { get; set; }
    public string Mode { get; set; }          // "task" | "free"
    public string? AppHint { get; set; }
    public int? TaskIndex { get; set; }
    public Guid SessionId { get; set; }

    public Capture(DateTimeOffset firstSeenAt, DateTimeOffset lastSeenAt, string text,
                   string mode, string? appHint, int? taskIndex, Guid sessionId)
    {
        FirstSeenAt = firstSeenAt; LastSeenAt = lastSeenAt; Text = text;
        Mode = mode; AppHint = appHint; TaskIndex = taskIndex; SessionId = sessionId;
    }

    public double Seconds => (LastSeenAt - FirstSeenAt).TotalSeconds;
}
```
In `IStore.cs`, add to the `IStore` interface:
```csharp
void AddCapture(Capture capture);
IReadOnlyList<Capture> Captures { get; }
Capture? LatestCaptureForSession(Guid sessionId);
```
and to `InMemoryStore`:
```csharp
private readonly List<Capture> _captures = new();
public void AddCapture(Capture capture) => _captures.Add(capture);
public IReadOnlyList<Capture> Captures => _captures;
public Capture? LatestCaptureForSession(Guid sessionId) =>
    _captures.Where(c => c.SessionId == sessionId)
             .OrderByDescending(c => c.LastSeenAt).FirstOrDefault();
```
(Add `using System.Linq;` / `using System;` if missing.)

**Step 4 — run, expect PASS.** `dotnet test windows/AccountaBall.Core.Tests` — new test green, baseline still green.

**Step 5 — commit.**
```bash
git add windows/AccountaBall.Core/Models/Capture.cs windows/AccountaBall.Core/Storage/IStore.cs windows/AccountaBall.Core.Tests/SpineSchemaTests.cs
git commit -m "feat(win/spine): add Capture model + IStore capture methods (Core)"
```

---

### Task W2: `Project` + `Thread` models + InMemoryStore + test

**Files:**
- Create: `windows/AccountaBall.Core/Models/Project.cs`
- Modify: `windows/AccountaBall.Core/Storage/IStore.cs`
- Modify: `windows/AccountaBall.Core.Tests/SpineSchemaTests.cs`

**Step 1 — failing test** (add a `[Fact]` to `SpineSchemaTests`):
```csharp
[Fact]
public void Project_OwnsThreads_AndPersistsViaStore()
{
    var store = new InMemoryStore();
    var project = new Project("Windows port");
    project.Threads.Add(new Thread("fix capture loop"));
    store.AddProject(project);
    store.Save();

    var p = Assert.Single(store.Projects);
    Assert.Equal("active", p.Status);
    Assert.Equal("open", p.Threads[0].Status);
}
```

**Step 2 — run, expect FAIL.**

**Step 3 — implement.** Create `Project.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace AccountaBall.Core.Models;

/// A long-lived workstream. Threads are owned (cascade) children. Part of the
/// Project -> Thread -> Contribution -> Capture context spine.
public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; }
    public List<string> Aliases { get; set; } = new();
    public string Status { get; set; } = "active";     // active | dormant | done
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastTouchedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Summary { get; set; } = "";
    public List<string> People { get; set; } = new();
    public List<string> CodeContext { get; set; } = new();
    public List<string> Refs { get; set; } = new();
    public List<Thread> Threads { get; } = new();

    public Project(string title) => Title = title;
}

/// An open loop under a project.
public sealed class Thread
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; }
    public string Status { get; set; } = "open";       // open | resolved
    public DateTimeOffset OpenedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }

    public Thread(string title) => Title = title;
}
```
> No `Foundation.Thread` clash in C# (this is a fresh type in `AccountaBall.Core.Models`).
In `IStore` add `void AddProject(Project project);` + `IReadOnlyList<Project> Projects { get; }`;
in `InMemoryStore` add a `List<Project> _projects` with the impls.

**Step 4 — run, expect PASS. Step 5 — commit** `"feat(win/spine): add Project + Thread models (Core)"`.

---

### Task W3: `Contribution` + `Judgment` models + InMemoryStore + test

**Files:** create `windows/AccountaBall.Core/Models/Contribution.cs`; modify `IStore.cs`; modify `SpineSchemaTests.cs`.

**Step 1 — failing test:**
```csharp
[Fact]
public void Contribution_And_Judgment_PersistViaStore()
{
    var store = new InMemoryStore();
    var pid = Guid.NewGuid(); var tid = Guid.NewGuid(); var sid = Guid.NewGuid();
    store.AddContribution(new Contribution(DateTimeOffset.UnixEpoch, pid, tid, sid, "task", 25, "Reshaped X"));
    store.AddJudgment(new Judgment(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch, "{\"project\":\"Windows port\"}", "accepted"));
    store.Save();
    Assert.Equal(25, Assert.Single(store.Contributions).Minutes);
    Assert.Equal("accepted", Assert.Single(store.Judgments).UserDecision);
}
```

**Step 2 — FAIL. Step 3 — implement** `Contribution.cs`:
```csharp
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
```
Add `AddContribution`/`Contributions` + `AddJudgment`/`Judgments` to `IStore` + `InMemoryStore`.

**Step 4 — PASS. Step 5 — commit** `"feat(win/spine): add Contribution + Judgment models (Core)"`.

---

### Task W4: stable `Id` on `WorkSession`

**Files:** modify `windows/AccountaBall.Core/Models/Persistence.cs`; modify `SpineSchemaTests.cs`.

**Step 1 — failing test:**
```csharp
[Fact]
public void WorkSession_HasStableDistinctIds()
{
    var a = new WorkSession(DateTimeOffset.UnixEpoch);
    var b = new WorkSession(DateTimeOffset.UnixEpoch);
    Assert.NotEqual(Guid.Empty, a.Id);
    Assert.NotEqual(a.Id, b.Id);
}
```

**Step 2 — FAIL** (`WorkSession` has no `Id`). **Step 3 — implement:** add to `WorkSession` (first property):
```csharp
public Guid Id { get; set; } = Guid.NewGuid();   // stable id for cross-session links (captures/contributions)
```
**Step 4 — PASS. Step 5 — commit** `"feat(win/spine): add stable Id to WorkSession (Core)"`.

---

### Task W5 (KEYSTONE): `AccountabilityEngine.IngestCapture` + dedup + test

Mirrors `FreeBallEngine.Ingest` and macOS `ingestCapture`. Task mode persists full
OCR into `Capture`, de-duped via `FreeBallDedup.IsSameScreen`, using the injectable
`Now` clock and the `IStore` latest-for-session query.

**Files:** modify `windows/AccountaBall.Core/Engine/AccountabilityEngine.cs`; create `windows/AccountaBall.Core.Tests/CaptureIngestTests.cs`.

**Step 1 — failing test** `CaptureIngestTests.cs`:
```csharp
using System;
using System.Linq;
using AccountaBall.Core.Models;
using AccountaBall.Core.Storage;
using Xunit;

namespace AccountaBall.Core.Tests;

public class CaptureIngestTests
{
    [Fact]
    public void IngestCapture_DedupsAndExtends_TaggedTask()
    {
        var (state, engine, store, clock) = EngineTest.MakeWithClock(null, "Ship the port");
        clock.Now = DateTimeOffset.UnixEpoch;
        engine.IngestCapture("editing AppController.cs line 1");
        clock.Now = clock.Now.AddSeconds(30);
        engine.IngestCapture("editing AppController.cs line 1 ");      // same -> extend
        clock.Now = clock.Now.AddSeconds(30);
        engine.IngestCapture("watching youtube basketball video");     // new -> insert

        var caps = store.Captures.Where(c => c.SessionId == engine.CurrentSession!.Id).ToList();
        Assert.Equal(2, caps.Count);
        Assert.All(caps, c => Assert.Equal("task", c.Mode));
        var first = caps.Single(c => c.Text.StartsWith("editing"));
        Assert.True(first.LastSeenAt > first.FirstSeenAt);            // extend branch ran
        Assert.True(first.Seconds >= 30);
    }
}
```
> Check `FreeBallDedup.IsSameScreen` treats `"…line 1"` vs `"…line 1 "` as same and the
> youtube string as different (it does for FreeBall's test). If thresholds differ,
> pick clearly-same / clearly-different strings but keep 3 calls → 2 rows.

**Step 2 — run, expect FAIL** (`IngestCapture` missing).

**Step 3 — implement.** Add to `AccountabilityEngine` (mirror `FreeBallEngine.Ingest`):
```csharp
/// Persist one screen read for long-term context (de-duped like FreeBall).
/// Called per cycle by the Platform capture loop after OCR; never blocks classify.
public void IngestCapture(string text, string? appHint = null)
{
    var session = CurrentSession;
    var store = Store;
    if (session is null || store is null) return;
    var ts = Now();
    var last = store.LatestCaptureForSession(session.Id);
    if (last is not null && FreeBallDedup.IsSameScreen(last.Text, text))
    {
        last.LastSeenAt = ts;
    }
    else
    {
        // TaskIndex is best-known-so-far (previous cycle's classification); may be
        // null in an active-but-taskless session. Not authoritative for this screen.
        store.AddCapture(new Capture(ts, ts, text, "task", appHint,
                                     CurrentTaskIndex, session.Id));
    }
    store.Save();
}
```
> Use whatever the engine's current-task-index accessor is (check how `Record(...)`
> obtains its `taskIndex` / the `AppState` active-task index — match that; the macOS
> equivalent was `state.activeTaskIndex`). If none is readily available, pass `null`
> for now and note it.

**Step 4 — run, expect PASS** — new test green, baseline still green (Core only).

**Step 5 — commit** `"feat(win/spine): task mode persists full OCR into Capture (Core keystone)"`.

---

### Task W6 (WINDOWS-ONLY — write here, COMPILE/VERIFY ON WINDOWS)

Cannot be compiled on macOS (net8.0-windows). Implement precisely; the user builds
the solution on Windows (`dotnet build windows/AccountaBall.sln`) to verify.

**6a — `SqliteStore` (Platform) implements the new `IStore` members.**
File: `windows/AccountaBall.Platform/Storage/SqliteStore.cs`. Add EF-backed impls
mirroring the existing methods:
```csharp
public void AddCapture(Capture capture) => _db.Set<Capture>().Add(capture);
public IReadOnlyList<Capture> Captures => _db.Set<Capture>().ToList();
public Capture? LatestCaptureForSession(Guid sessionId) =>
    _db.Set<Capture>().Where(c => c.SessionId == sessionId)
       .OrderByDescending(c => c.LastSeenAt).FirstOrDefault();
public void AddProject(Project p) => _db.Set<Project>().Add(p);
public IReadOnlyList<Project> Projects => _db.Set<Project>().ToList();
public void AddContribution(Contribution c) => _db.Set<Contribution>().Add(c);
public IReadOnlyList<Contribution> Contributions => _db.Set<Contribution>().ToList();
public void AddJudgment(Judgment j) => _db.Set<Judgment>().Add(j);
public IReadOnlyList<Judgment> Judgments => _db.Set<Judgment>().ToList();
```
(Match `SqliteStore`'s actual field name for the `DbContext`; the existing methods
show it.)

**6b — `AccountaBallDbContext` registers the entities.**
File: `windows/AccountaBall.Platform/Storage/AccountaBallDbContext.cs`.
- Add DbSets: `Captures`, `Projects`, `Contributions`, `Judgments` (Threads are owned
  via Project).
- In `OnModelCreating`, add fluent config mirroring the existing entries:
  `Capture` key `Id`; `Project` key `Id` + `HasMany(p => p.Threads).WithOne().OnDelete(Cascade)`
  + `OwnsMany`-to-JSON (or value-conversion) for the `List<string>` props (`Aliases`,
  `People`, `CodeContext`, `Refs`) — follow how `FreeBallSession.Categories` uses
  `OwnsMany(... o => o.ToJson())`; `Thread` key `Id`; `Contribution` key `Id`;
  `Judgment` key `Id`. Also add `e.Property(s => s.Id)` to the `WorkSession` config
  now that it has an explicit `Id` (make `Id` the key).
- This is an additive schema change. Note: EF will need a **migration** (or
  `EnsureCreated` for a dev DB). If the project uses EF migrations, add one
  (`dotnet ef migrations add SpineM1`); if it uses `EnsureCreated`/no migrations,
  a fresh dev DB picks up the new tables. Confirm which approach the project uses.

**6c — `AppController` wires the keystone.**
File: `windows/AccountaBall.App/Shell/AppController.cs`, in `AccountabilityTickAsync`
(~line 134, right after `var text = await _ocr.RecognizeTextAsync(frame.Bitmap);`),
add: `_engine.IngestCapture(text);` (mirrors `FreeBallTickAsync`'s
`_freeBall.Ingest(text);`). Place it before `ProcessCycle` so the capture is stored
regardless of classification outcome.

**6d — Verify on Windows:** `dotnet build windows/AccountaBall.sln` (0 errors),
`dotnet test windows/AccountaBall.Core.Tests` (Core green), run the app, declare a
task, work ~30s across a couple of windows, confirm `Capture` rows with
`Mode == "task"` accumulate + dedup in the SQLite DB / logs.

**Commit** (after Windows verification) `"feat(win/spine): wire DbContext + SqliteStore + capture loop (M1 Platform/App)"`.

---

## Done-when (Windows M1 exit criteria)

- [ ] `dotnet test windows/AccountaBall.Core.Tests` green (baseline + new SpineSchema/CaptureIngest), W1–W5 verified on macOS.
- [ ] `Capture`/`Project`/`Thread`/`Contribution`/`Judgment` exist in Core; `WorkSession` has `Id`.
- [ ] `IngestCapture` dedups + tags `"task"` + extends `LastSeenAt` (Core test).
- [ ] (Windows) solution builds; DbContext has the new tables; `AppController` calls `IngestCapture`; a real session writes deduped `Mode=="task"` captures.

## Deferred (same as macOS M1)
- Converge FreeBall onto the shared `Capture` (retire `FreeBallCapture`).
- M2 entity resolution + confirm UI + Judgment logging; M3 recall.
- Index `Capture.SessionId` (EF supports `HasIndex` — cheaper here than on macOS 14;
  consider adding in 6b: `e.HasIndex(c => new { c.SessionId, c.LastSeenAt })`).
