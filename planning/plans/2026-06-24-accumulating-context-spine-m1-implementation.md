# Accumulating Context Spine — Milestone 1 (Data Foundation) Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add the durable Project→Thread→Contribution spine + a Judgment log to the
macOS app, and make task mode stop discarding screen OCR — so every working hour
(task *or* FreeBall) builds the same high-fidelity, accumulating context.

**Architecture:** Pure-additive SwiftData schema. New `Capture` table both modes
write to; new `Project` / `Thread` / `Contribution` / `Judgment` entities. **No AI
and no UI in M1** — those are M2/M3. **No migration of existing stores** — every
change is additive, so existing FreeBall/Work data keeps loading. Physically
merging the legacy `FreeBallCapture` into `Capture` is deferred (last task notes
it) to keep working FreeBall untouched in this slice.

**Tech Stack:** Swift 6 (v5 language mode), SwiftData (macOS 14+), custom
MicroTest runner (`make test`).

**Reference design:** `planning/plans/2026-06-24-accumulating-context-spine-design.md`

**Repo:** All code paths are in the **separate** `src/` repo
(`jecohub/AccountaBall-macOS`), currently on `master`. Run a worktree there
(see "Setup") — note `Sources/AccountaBall/Views/SessionBallView.swift` has an
uncommitted panel fix in the main checkout; a worktree branches from HEAD so it
won't collide, but that fix is unmerged.

---

## Setup (before Task 1)

Work in an isolated worktree of the **src/** repo:

```bash
cd /Users/jericodelacruz/Desktop/AccountaBall/src
git worktree add ../src-spine-m1 -b spine-m1
cd ../src-spine-m1
make test   # baseline: confirm all existing suites pass before changing anything
```
Expected: `✓ All N tests passed`.

**Test idiom (read once):** every suite is a top-level `func runXxxTests()` (or
`@MainActor async`) using `expect(cond, "msg")`, defined in
`Tests/AccountaBallTests/XxxTests.swift`, and **registered** in
`Tests/TestRunner/main.swift`'s `runAllTests()`. New suites are invisible until
registered. There is one runner: `make test` runs everything. To "see it fail,"
run `make test` and look for your new suite's ❌ lines.

---

## Schema decisions (read before coding)

- **`Capture`** (new, shared): `id: UUID`, `firstSeenAt: Date`, `lastSeenAt: Date`,
  `text: String`, `mode: String` (`"task"` | `"free"`), `appHint: String?`,
  `taskIndex: Int?`, `sessionId: UUID` (links to the owning WorkSession/FreeBallSession).
  Mirrors `FreeBallCapture` + mode/app/task/session fields.
- **`Project`**: `id: UUID`, `title: String`, `aliases: [String]`,
  `status: String` (`"active"|"dormant"|"done"`), `createdAt: Date`,
  `lastTouchedAt: Date`, `summary: String`, `people: [String]`,
  `codeContext: [String]`, `refs: [String]`,
  `@Relationship(deleteRule: .cascade) threads: [Thread]`.
- **`Thread`**: `id: UUID`, `title: String`, `status: String` (`"open"|"resolved"`),
  `openedAt: Date`, `resolvedAt: Date?`. (Owned by Project via the cascade above;
  no explicit inverse needed for M1.)
- **`Contribution`**: `id: UUID`, `at: Date`, `projectId: UUID`,
  `threadId: UUID?`, `sessionId: UUID`, `sessionKind: String` (`"task"|"free"`),
  `minutes: Int`, `summary: String`. (Cross-type join → UUID refs, not relationships.)
- **`Judgment`**: `id: UUID`, `at: Date`, `captureRangeStart: Date`,
  `captureRangeEnd: Date`, `modelProposal: String` (JSON), `userDecision: String`.
- **`WorkSession`** gains `var id: UUID = UUID()` (FreeBallSession already has one)
  so captures/contributions can reference it.

All new `@Model` types are `@available(macOS 14, *)` and added to the `Schema([...])`
array in `AccountaBallStore.makeContainer`.

---

### Task 1: `Capture` shared model

**Files:**
- Create: `Sources/AccountaBall/Models/Capture.swift`
- Modify: `Sources/AccountaBall/Models/Persistence.swift` (add `Capture.self` to `Schema`)
- Test: `Tests/AccountaBallTests/SpineSchemaTests.swift` (new)
- Modify: `Tests/TestRunner/main.swift` (register `runSpineSchemaTests()`)

**Step 1: Write the failing test**

Create `Tests/AccountaBallTests/SpineSchemaTests.swift`:
```swift
import SwiftData
@testable import AccountaBall

@MainActor
func runSpineSchemaTests() {
    suite("SpineSchema") {
        guard let container = try? AccountaBallStore.makeContainer(inMemory: true) else {
            expect(false, "container builds"); return
        }
        let ctx = container.mainContext
        let sid = UUID()
        let cap = Capture(firstSeenAt: .now, lastSeenAt: .now, text: "hello",
                          mode: "task", appHint: "Xcode", taskIndex: 0, sessionId: sid)
        ctx.insert(cap)
        try? ctx.save()
        let fetched = (try? ctx.fetch(FetchDescriptor<Capture>())) ?? []
        expect(fetched.count == 1, "one Capture persisted")
        expect(fetched.first?.mode == "task", "mode round-trips")
        expect(fetched.first?.taskIndex == 0, "taskIndex round-trips")
        expect(fetched.first?.sessionId == sid, "sessionId round-trips")
    }
}
```
Register it: in `Tests/TestRunner/main.swift` add `runSpineSchemaTests()` inside `runAllTests()`.

**Step 2: Run to verify it fails**

Run: `make test`
Expected: FAIL — compile error "cannot find 'Capture' in scope".

**Step 3: Minimal implementation**

Create `Sources/AccountaBall/Models/Capture.swift`:
```swift
import SwiftData
import Foundation

/// One screen read, shared by both modes. Task mode no longer discards OCR;
/// FreeBall continues to keep full text. De-dup collapses unchanged screens into
/// a firstSeenAt…lastSeenAt span.
@available(macOS 14, *)
@Model
final class Capture {
    var id: UUID = UUID()
    var firstSeenAt: Date
    var lastSeenAt: Date
    var text: String
    var mode: String          // "task" | "free"
    var appHint: String?
    var taskIndex: Int?
    var sessionId: UUID
    init(firstSeenAt: Date, lastSeenAt: Date, text: String, mode: String,
         appHint: String? = nil, taskIndex: Int? = nil, sessionId: UUID) {
        self.firstSeenAt = firstSeenAt; self.lastSeenAt = lastSeenAt
        self.text = text; self.mode = mode; self.appHint = appHint
        self.taskIndex = taskIndex; self.sessionId = sessionId
    }
    var seconds: TimeInterval { lastSeenAt.timeIntervalSince(firstSeenAt) }
}
```
Add `Capture.self` to the `Schema([...])` array in `Persistence.swift`.

**Step 4: Run to verify it passes**

Run: `make test`
Expected: PASS — `SpineSchema` suite green, all prior suites still green.

**Step 5: Commit**
```bash
git add Sources/AccountaBall/Models/Capture.swift Sources/AccountaBall/Models/Persistence.swift Tests/AccountaBallTests/SpineSchemaTests.swift Tests/TestRunner/main.swift
git commit -m "feat(spine): add shared Capture model"
```

---

### Task 2: `Project` + `Thread` models with cascade

**Files:**
- Create: `Sources/AccountaBall/Models/Project.swift`
- Modify: `Persistence.swift` (`Schema` += `Project.self`, `Thread.self`)
- Modify: `Tests/AccountaBallTests/SpineSchemaTests.swift` (extend suite)

**Step 1: Extend the failing test** — add to the `SpineSchema` suite body:
```swift
let project = Project(title: "Windows port")
let thread = Thread(title: "fix panel sizing")
project.threads.append(thread)
ctx.insert(project)
try? ctx.save()
let projects = (try? ctx.fetch(FetchDescriptor<Project>())) ?? []
expect(projects.count == 1, "one Project persisted")
expect(projects.first?.threads.count == 1, "thread related via cascade")
expect(projects.first?.status == "active", "default status active")
expect(projects.first?.threads.first?.status == "open", "default thread status open")
```

**Step 2: Run to verify it fails** — `make test` → FAIL "cannot find 'Project'/'Thread'".

**Step 3: Minimal implementation** — create `Sources/AccountaBall/Models/Project.swift`:
```swift
import SwiftData
import Foundation

@available(macOS 14, *)
@Model
final class Project {
    var id: UUID = UUID()
    var title: String
    var aliases: [String] = []
    var status: String = "active"          // active | dormant | done
    var createdAt: Date = Date()
    var lastTouchedAt: Date = Date()
    var summary: String = ""
    var people: [String] = []
    var codeContext: [String] = []
    var refs: [String] = []
    @Relationship(deleteRule: .cascade) var threads: [Thread] = []
    init(title: String) { self.title = title }
}

@available(macOS 14, *)
@Model
final class Thread {
    var id: UUID = UUID()
    var title: String
    var status: String = "open"            // open | resolved
    var openedAt: Date = Date()
    var resolvedAt: Date? = nil
    init(title: String) { self.title = title }
}
```
Add `Project.self, Thread.self` to the `Schema`.

> Naming note: `Thread` shadows `Foundation.Thread`. It's only used as a `@Model`
> here and the app never touches `Foundation.Thread`; if the compiler complains in
> any file, qualify as `AccountaBall.Thread`. Acceptable for M1.

**Step 4: Run to verify it passes** — `make test` → PASS.

**Step 5: Commit**
```bash
git add Sources/AccountaBall/Models/Project.swift Sources/AccountaBall/Models/Persistence.swift Tests/AccountaBallTests/SpineSchemaTests.swift
git commit -m "feat(spine): add Project + Thread models with cascade"
```

---

### Task 3: `Contribution` + `Judgment` models

**Files:**
- Create: `Sources/AccountaBall/Models/Contribution.swift`
- Modify: `Persistence.swift` (`Schema` += `Contribution.self`, `Judgment.self`)
- Modify: `SpineSchemaTests.swift` (extend suite)

**Step 1: Extend the failing test:**
```swift
let contrib = Contribution(at: .now, projectId: project.id, threadId: thread.id,
                           sessionId: sid, sessionKind: "task", minutes: 25,
                           summary: "Reshaped the confirm card")
let judgment = Judgment(at: .now, captureRangeStart: .now, captureRangeEnd: .now,
                        modelProposal: "{\"project\":\"Windows port\"}",
                        userDecision: "accepted")
ctx.insert(contrib); ctx.insert(judgment)
try? ctx.save()
expect(((try? ctx.fetch(FetchDescriptor<Contribution>())) ?? []).first?.minutes == 25, "contribution minutes round-trip")
expect(((try? ctx.fetch(FetchDescriptor<Judgment>())) ?? []).first?.userDecision == "accepted", "judgment decision round-trip")
```

**Step 2: Run to verify it fails** — `make test` → FAIL "cannot find 'Contribution'/'Judgment'".

**Step 3: Minimal implementation** — create `Sources/AccountaBall/Models/Contribution.swift`:
```swift
import SwiftData
import Foundation

/// Join between a session and a project/thread: what this session advanced.
@available(macOS 14, *)
@Model
final class Contribution {
    var id: UUID = UUID()
    var at: Date
    var projectId: UUID
    var threadId: UUID?
    var sessionId: UUID
    var sessionKind: String     // "task" | "free"
    var minutes: Int
    var summary: String
    init(at: Date, projectId: UUID, threadId: UUID?, sessionId: UUID,
         sessionKind: String, minutes: Int, summary: String) {
        self.at = at; self.projectId = projectId; self.threadId = threadId
        self.sessionId = sessionId; self.sessionKind = sessionKind
        self.minutes = minutes; self.summary = summary
    }
}

/// One model-proposal + user-decision pair from session-end entity resolution.
/// The learning signal + eval set for the trust ladder (see design §3, §5).
@available(macOS 14, *)
@Model
final class Judgment {
    var id: UUID = UUID()
    var at: Date
    var captureRangeStart: Date
    var captureRangeEnd: Date
    var modelProposal: String   // JSON
    var userDecision: String
    init(at: Date, captureRangeStart: Date, captureRangeEnd: Date,
         modelProposal: String, userDecision: String) {
        self.at = at; self.captureRangeStart = captureRangeStart
        self.captureRangeEnd = captureRangeEnd
        self.modelProposal = modelProposal; self.userDecision = userDecision
    }
}
```
Add `Contribution.self, Judgment.self` to the `Schema`.

**Step 4: Run to verify it passes** — `make test` → PASS.

**Step 5: Commit**
```bash
git add Sources/AccountaBall/Models/Contribution.swift Sources/AccountaBall/Models/Persistence.swift Tests/AccountaBallTests/SpineSchemaTests.swift
git commit -m "feat(spine): add Contribution + Judgment models"
```

---

### Task 4: Give `WorkSession` a stable id

**Files:**
- Modify: `Sources/AccountaBall/Models/Persistence.swift` (`WorkSession`)
- Modify: `SpineSchemaTests.swift` (assert id exists/stable)

**Step 1: Extend the failing test:**
```swift
let ws = WorkSession(startedAt: .now)
ctx.insert(ws); try? ctx.save()
let wsId = ws.id
let refetched = (try? ctx.fetch(FetchDescriptor<WorkSession>()))?.first
expect(refetched?.id == wsId, "WorkSession id is stable across fetch")
```

**Step 2: Run to verify it fails** — `make test` → FAIL "value of type 'WorkSession' has no member 'id'".

**Step 3: Minimal implementation** — in `Persistence.swift`, add to `WorkSession`:
```swift
var id: UUID = UUID()
```
(Place it directly under the class's opening; the default initializer assignment
covers existing rows on load.)

**Step 4: Run to verify it passes** — `make test` → PASS (existing WorkSession/Persistence suites still green).

**Step 5: Commit**
```bash
git add Sources/AccountaBall/Models/Persistence.swift Tests/AccountaBallTests/SpineSchemaTests.swift
git commit -m "feat(spine): add stable id to WorkSession"
```

---

### Task 5: Task mode persists full OCR into `Capture` (the core fidelity fix)

**Goal:** the every-cycle OCR text task mode already computes
(`AccountabilityEngine.swift:90-92`) is now stored as a `Capture` (de-duped),
instead of only living in `lastScreenText`. This is the change that makes task
mode's data as rich as FreeBall's.

**Files:**
- Modify: `Sources/AccountaBall/Engine/AccountabilityEngine.swift`
- Test: `Tests/AccountaBallTests/CaptureIngestTests.swift` (new) + register in `main.swift`

**Step 1: Write the failing test** — create `Tests/AccountaBallTests/CaptureIngestTests.swift`:
```swift
import SwiftData
@testable import AccountaBall

@MainActor
func runCaptureIngestTests() async {
    await suite("CaptureIngest") {
        guard let container = try? AccountaBallStore.makeContainer(inMemory: true) else {
            expect(false, "container builds"); return
        }
        let engine = makeTestEngine(container: container)   // see TestFakes helper
        engine.startSessionForTest()                        // inserts WorkSession, sets currentSession

        engine.ingestCapture(text: "screen A")              // new screen -> new row
        engine.ingestCapture(text: "screen A")              // same -> extend, no new row
        engine.ingestCapture(text: "totally different B")   // new screen -> new row

        let caps = (try? container.mainContext.fetch(FetchDescriptor<Capture>())) ?? []
        expect(caps.count == 2, "dedup collapses identical consecutive screens")
        expect(caps.allSatisfy { $0.mode == "task" }, "task-mode captures tagged task")
        expect(caps.allSatisfy { $0.sessionId == engine.currentSession?.id }, "captures linked to session")
    }
}
```
Register `await runCaptureIngestTests()` in `main.swift` (it's async — call it where the other `await runEngine…` suites are).

> **TestFakes:** `makeTestEngine(container:)` and `startSessionForTest()` may not
> exist. Check `Tests/AccountaBallTests/TestFakes.swift` and
> `AccountabilityEngine`'s existing test seams (it already exposes
> `modelContext`/`currentSession` and other engines have `beginForTest()`). Add a
> thin `startSessionForTest()` to the engine mirroring `FreeBallEngine.beginForTest()`
> if absent, and a `makeTestEngine` fake using existing fake services
> (see how `AccountabilityEngineTests.swift` builds an engine — reuse that).

**Step 2: Run to verify it fails** — `make test` → FAIL ("cannot find 'ingestCapture'"/missing seam).

**Step 3: Minimal implementation** — add to `AccountabilityEngine`:
```swift
/// Persist one screen read for long-term context (de-duped like FreeBall).
/// Called from the capture loop after OCR; never blocks classification.
func ingestCapture(text: String, appHint: String? = nil) {
    guard let session = currentSession, let ctx = modelContext else { return }
    let now = Date()
    let mine = (try? ctx.fetch(FetchDescriptor<Capture>()))?
        .filter { $0.sessionId == session.id }
    if let last = mine?.max(by: { $0.lastSeenAt < $1.lastSeenAt }),
       FreeBallDedup.isSameScreen(last.text, text) {
        last.lastSeenAt = now
    } else {
        let cap = Capture(firstSeenAt: now, lastSeenAt: now, text: text,
                          mode: "task", appHint: appHint,
                          taskIndex: state.activeTaskIndex, sessionId: session.id)
        ctx.insert(cap)
    }
    try? ctx.save()
}
```
Then in the capture loop (`AccountabilityEngine.swift` ~line 92, right after
`self.lastScreenText = text`), add:
```swift
self.ingestCapture(text: text)
```
Add a `startSessionForTest()` seam if the test needs it (mirror the existing
`startSession` private path used at lines 304-309).

**Step 4: Run to verify it passes** — `make test` → PASS. Confirm no existing engine suite regressed.

**Step 5: Commit**
```bash
git add Sources/AccountaBall/Engine/AccountabilityEngine.swift Tests/AccountaBallTests/CaptureIngestTests.swift Tests/TestRunner/main.swift
git commit -m "feat(spine): task mode persists full OCR into Capture (unified fidelity)"
```

---

### Task 6: Manual smoke + progress note

**Files:**
- Modify: `planning/plans/2026-06-24-accumulating-context-spine-design.md` (add a
  short "BUILD PROGRESS" note: M1 schema + unified task capture done).

**Step 1:** Build and run the real app, start a task session, let a few cycles
run, then inspect the store:
```bash
make build && make run    # declare a task, work ~30s, then quit
```
Expected (via debug log / a one-off fetch): `Capture` rows with `mode == "task"`
accumulating and de-duping on static screens.

**Step 2:** Add the BUILD PROGRESS note and commit:
```bash
git add planning/plans/2026-06-24-accumulating-context-spine-design.md
git commit -m "docs(spine): record M1 data-foundation completion"
```

> NOTE: the design doc lives in the **AccountaBall** repo, not `src/`. Make this
> doc commit there, not in the `src-spine-m1` worktree.

---

## Deferred to later milestones (NOT in M1)

- **Converge FreeBall onto `Capture`.** Migrate `FreeBallEngine.ingest` + `end()`
  transcript assembly off `FreeBallCapture` onto the shared `Capture` (mode
  `"free"`), then retire `FreeBallCapture` with a SwiftData migration. Carries
  migration risk — own task with its own test for old-store loading.
- **M2 — Entity resolution + confirm UI + Judgment logging.** Session-end AI pass
  proposes Project/Thread attachments; recap UI lets the user accept/rename/merge/
  close; each decision writes a `Judgment`. (Hooks into `finalizeSessionRecap`
  and `FreeBallEngine.end`.)
- **M3 — Recall.** Query layer + a "what was I doing on X" surface over the spine.
- **Trust ladder + reconstruction test** (design §5) — once Judgments exist.
- **Encryption at rest + capture exclusions** (design §4).

---

## Done-when (M1 exit criteria)

- [ ] `make test` green, including new `SpineSchema` + `CaptureIngest` suites.
- [ ] `Project`, `Thread`, `Contribution`, `Judgment`, `Capture` persist + relate.
- [ ] A real task session writes de-duped `Capture` rows (`mode == "task"`).
- [ ] Zero migration breakage: an existing store still loads (additive-only).
- [ ] No added latency to the 5s loop (capture persist is a cheap insert/save).
