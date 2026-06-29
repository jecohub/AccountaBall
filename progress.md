# AccountaBall — Progress Notes
_Last updated: 2026-06-24 (Context Spine M1 — data foundation: Tasks 1–5 built & reviewed, Task 5 mid-review)_

> **How the whole app operates:** see [docs/HOW-IT-WORKS.md](docs/HOW-IT-WORKS.md)
> — the single end-to-end operational reference (lifecycle, monitoring loop,
> 3-state model, drift engine, suppression mechanisms, data, providers, code map).

---

## Context Spine M1 — Data Foundation (IN PROGRESS)

Building toward an accumulating context graph so AccountaBall can eventually
**recall → suggest → act** on all the user's work (design goal: enough captured
data to one day "do it for me / suggest things"). Applies to both FreeBall and
task mode.

- **Design (approved):** [`planning/plans/2026-06-24-accumulating-context-spine-design.md`](planning/plans/2026-06-24-accumulating-context-spine-design.md)
  — Project→Thread→Contribution spine, unified full-OCR capture across both modes,
  AI-proposes/user-confirms entity formation + a Judgment learning loop + per-project
  trust ladder, and a reconstruction test to measure data sufficiency.
- **M1 plan (data foundation, additive-only — no AI/UI/migration):**
  [`planning/plans/2026-06-24-accumulating-context-spine-m1-implementation.md`](planning/plans/2026-06-24-accumulating-context-spine-m1-implementation.md)

**Where the code lives:** the inner `src/` repo (`jecohub/AccountaBall-macOS`), in an
isolated worktree at `../src-spine-m1` on branch **`spine-m1`** (branched from
`src` master `1f5c3cb`). Subagent-driven (implementer + spec review + code-quality
review per task). NOT yet merged to `src` master.

**Done & reviewed (TDD, `make test` green at each step):**

| Task | What | Commit | Tests |
|---|---|---|---|
| 1 | Shared `Capture` model (full OCR + mode/appHint/taskIndex/sessionId) + schema | `cf2f64b` | 388 |
| 2 | `Project` + `Thread` (cascade) | `bee44cf` | 394 |
| 3 | `Contribution` + `Judgment` (UUID soft-refs, not @Relationship) | `b9aba99` | 401 |
| 4 | Stable `id: UUID` on `WorkSession` | `01156df` | 403 |
| 5 | **Keystone:** task mode persists full OCR into `Capture` (de-duped like FreeBall) — stops discarding screen text | `c8cffa0` | 406 |

All new entities registered in `AccountaBallStore` schema; `runSpineSchemaTests` +
`CaptureIngestTests` cover round-trips, cascade-delete, and dedup (3 calls → 2 rows).

Task 5's two code-quality findings were both fixed (clock → injectable `now()`;
the full-table fetch → scoped single-row `#Predicate(sessionId) + sortBy lastSeenAt
desc + fetchLimit 1`) and re-reviewed. Fix commit `b776f1e`, tests 408.

**Final holistic review: READY TO MERGE** (`1f5c3cb..b776f1e`, no critical/blocking
issues; all 6 design invariants honored; 408 green).

**Resume here:**
1. **Manual GUI smoke (needs the user)** — `make build && make run` in the worktree,
   declare a task, work ~30s across a couple of screens, quit; confirm `Capture` rows
   with `mode == "task"` accumulate + dedup. Can't be done headlessly (Screen
   Recording permission + GUI). The unit test (`CaptureIngestTests`) already proves
   the `ingestCapture` logic; this just confirms the live loop→OCR→store wiring.
2. **Merge** `spine-m1` → `src` master (finishing-a-development-branch).

**Deferred follow-ups (recorded, not blocking M1):**
- **Index `Capture.sessionId` (+ `lastSeenAt`)** — `ingestCapture` queries by
  `sessionId` every cycle over an unbounded table. NOT doable declaratively on the
  macOS 14 floor: `@Attribute(.indexed)` doesn't exist at 14 and `#Index` is macOS
  15+ (verified by compile test). Revisit when min target rises to 15, or mitigate
  another way. The `fetchLimit 1` already bounds the result row count.
- **For the M2 planner:** add `sessionId: UUID` to `Judgment` (so a capture-range
  query can be session-scoped — additive, no rows exist yet); and note the
  deliberate `Contribution.minutes: Int` vs `Capture.seconds: TimeInterval`
  granularity (lossy rollup decided on purpose, not a bug).

**Deferred to later milestones (NOT M1):** converge FreeBall onto the shared
`Capture` (retire `FreeBallCapture`, needs migration); M2 entity resolution +
confirm UI + Judgment logging; M3 recall; trust ladder + reconstruction test;
encryption at rest + capture exclusions.

> Note: the macOS `SessionBallView` allowance-confirm panel-size fix (compact
> rounded card instead of full-panel black slab) shipped separately on `src`
> master (`332ce78`, pushed).

### Windows port of M1 — built (Core verified, W6 needs a Windows compile)

Ported to the C#/.NET Windows port to keep it at parity. Plan:
[`planning/plans/2026-06-24-windows-spine-m1-port-implementation.md`](planning/plans/2026-06-24-windows-spine-m1-port-implementation.md).
On branch `windows-port-m3-m4` (this repo), subagent-driven + reviewed per task.
**Committed locally, not pushed.**

- **W1–W5 (`AccountaBall.Core`, net8.0):** built **and tested on macOS** —
  `dotnet test windows/AccountaBall.Core.Tests` = **118 green**. Adds POCOs
  `Capture`/`Project`/`Thread`/`Contribution`/`Judgment`, the `IStore` capture/
  spine methods + `InMemoryStore` impls, `WorkSession.Id`, and the keystone
  `AccountabilityEngine.IngestCapture` (de-dup via `FreeBallDedup`, injectable
  `Now`, scoped `LatestCaptureForSession`). Final review: ready to merge.
- **W6 (`AccountaBall.Platform` + `.App`, net8.0-windows):** **written but NOT
  compiled** (can't build Windows targets on the macOS box). `AccountaBallDbContext`
  DbSets + EF config (Capture indexed on `(SessionId, LastSeenAt)`; WorkSession key
  → its new Guid); `SqliteStore` EF-backed `IStore` impls; `AppController` calls
  `IngestCapture(text, frame.WindowTitle)` after OCR, before classify.

**Resume on a Windows machine:**
1. `dotnet build windows/AccountaBall.sln` (0 errors) + `dotnet test windows/AccountaBall.Core.Tests`.
2. **Delete any existing dev DB** (`%LOCALAPPDATA%\AccountaBall\accountaball.db`) —
   `EnsureCreated` does NOT migrate in place, so a stale DB silently lacks the new
   tables + Guid key and would fail late on first `Captures` access.
3. Run the app, declare a task, work ~30s across a couple of windows; confirm
   `Capture` rows with `Mode=="task"` accumulate + dedup.
4. (Recommended, from final review) add ONE Windows-only SqliteStore integration
   test: a 2nd `IngestCapture` of the same screen extends `LastSeenAt` (UPDATE, not
   a 2nd row) — the change-tracking-dependent extend branch is unexercised by the
   Core/InMemoryStore tests.

---

## v3.2 — 3-State Accountability Redesign (Phase 1 BUILT)

A major evolution: from a binary on/off nag into a calm "mirror" that classifies
ON / AMBIGUOUS / OFF, makes drift conscious without shaming, and tracks a
pre-committed drift limit toward an honor-system consequence (visible recap record).

**Design APPROVED** (Sections 1–4):
[`planning/plans/2026-06-07-3state-accountability-design.md`](planning/plans/2026-06-07-3state-accountability-design.md).
Decisions: honor-system consequence; pre-commit = setup-locked drift limit
(default 3) + (Phase 2) optional commitment line + cross-session streak;
architecture B (model perceives 3-state, Swift owns the deterministic engine);
3-phase rollout.

**Phase 1 (the spine) — BUILT** per
[`docs/plans/2026-06-07-3state-phase1.md`](docs/plans/2026-06-07-3state-phase1.md)
(16 tasks, subagent-driven, two-stage reviewed + a final holistic review). Delivered:
`.ambiguous` state + 3-state classify prompt with bias-toward-ON/AMBIGUOUS (both
providers); `JustificationEvent.kind` unified check log; **derived** tamper-proof
`driftCount` + `commitmentBroken` vs a setup-locked drift-limit stepper; the
AMBIGUOUS ask-once card (accept→allowance, reject→drift); the OFF calm break/resume
choice (no excuse typing); a timed 5-min break with a ball countdown; auto-return
logging for ignored prompts; and the session-recap **transparency log** + "Drift N
of LIMIT" + firm broken-commitment line. Tone reframed to "mirror, not boss".

Verification: **~299 unit tests** (`make -C src test`) + a **live Ollama 3-state-bias
integration test** (passed on `qwen2.5:7b`); `make -C src build` clean. Code is on
`master` in the inner `src/` repo.

**Next:** manual "feel it" run — checklist in
[`v3.2-phase1-manual-qa.md`](v3.2-phase1-manual-qa.md) (GUI/animation/permission/
timing + a "how it feels" section to capture Phase 2 signal). Then **Phase 2**
(cross-session streak, optional commitment line, adaptive cadence consuming model
confidence, anti-gaming). Known Phase-1 limitation: the AMBIGUOUS ask is
first-task-centric.

**Retired/dormant from earlier versions:** the typed-excuse path
(`handleExcuse`/`evaluateExcuse`) and the "give me 2 minutes" activity grace are
retained for tests but no longer wired into the live flow (replaced by the AMBIGUOUS
ask and the timed break).

---

## v3.1.1 — Post-QA bug fixes (IN PROGRESS)

First hands-on QA of v3.1 surfaced 7 real bugs. Root causes were traced from the
code + the debug log (`~/Library/Application Support/AccountaBall/logs/accountaball.log`)
+ three screenshots. Status of each:

| # | Symptom | Root cause | Fix | Status |
|---|---|---|---|---|
| 1 | Session completion shows **no breakdown** | `engine.beginSession()` **never called in production** (only `state.startSession()` was) → `engine.currentSession` always nil → `record()` / `finalizeSessionRecap()` / `summarizeCompletion()` all early-return. The whole v3 timeline/recap/history layer was dead in the real app. | Call `engine.beginSession(tasks:)` in `TaskSetupView.handleLetsGo()`; `endSession()` after the recap in the AppDelegate phase-watcher. | ✅ done (uncommitted) |
| 2 | Completion shows **"Total time --:--"** | `completeTaskAt()` calls `endSession()` (nils `sessionStartTime`) before `.complete`. | Snapshot `AppState.lastSessionDuration` before `endSession()`; CompletionView reads it. | ✅ done |
| 3 | **"AI unavailable" card flapped ~18×** with Ollama running | Capture loop treated a cancelled request (`-999 cancelled` — a normal lifecycle cancel) as a provider outage. | `AccountabilityEngine.isBenignCancellation()`; the loop skips cancellations instead of entering `.aiUnavailable`. | ✅ done |
| 4 | **Session log half-transparent** (white desktop bleeds through) | Views used `Color.black.opacity(0.85)`, and `AccountaProgressView` placed it as a VStack **sibling** (not a backdrop). | Opaque `Color.black` everywhere; `AccountaProgressView` rebuilt as a ZStack with a backdrop layer. | ✅ done |
| 5 | Task-match "you did this before" card **pushes buttons off-screen** | `.setup` panel fixed at 520×440; inline card overflows; panel sizes not clamped to the display. | Clamp all `FloatingPanel` sizes to the screen's visibleFrame; taller setup panel; rows now in a `ScrollView` with the "Let's go!" button pinned below it. | ✅ done |
| 6 | App **unusable when launched with Ollama down** | `recoverFromAIUnavailable()` always jumped to `.session` even with no session started. | Remember `phaseBeforeUnavailable`; recovery returns there (`.welcome` on a fresh launch). | ✅ done |
| 7 | Excuse could get **stuck**; no accepted/escape feedback | `handleExcuse` only resumed when the verdict had a `taskIndex`; the "accepted" stage was dead code, and after `handleExcuse` stopped flipping phase the justified path resumed nothing — the view froze. | `handleExcuse` no longer flips phase (resume moved to the view) + `resumeAfterExcuse(graceSeconds:)`. OffTaskView now shows an **accepted** stage (👍 "Carry on" → "Got it" resumes) and a **rejected** stage with "Back to it" + a **"Continue anyway"** escape hatch (resumes with a 120s grace via `AppConstants.continueAnywayGraceSeconds`). | ✅ done |

**Product decision (excuse policy):** keep the model's honest judgment (the log
shows it correctly rejecting "just checking linkedin" etc.) but add a "Continue
anyway" escape hatch on rejection + clear accepted/rejected feedback.

**All 7 bugs now fixed.** Verification (all green):
- `make test` → **233/233** passing (added a grace-window suite asserting "Continue
  anyway" suppresses past the default 15s settle but re-prompts after the 120s grace).
- `make test-integration` → **10/10** live `qwen2.5:7b` tests passing.
- `make build` → clean.

**Still TODO before this is shippable:**
- Run the human-eyes pass in `v3.1-manual-qa.md` (Screen Recording + Ollama, then
  repeat with `AI_PROVIDER=openrouter`).
- **Security:** rotate the leaked v2 OpenRouter key at openrouter.ai (in git history
  before commit `9c4ddf3`).

---

## v3.1 — Provider availability + settle window + session breakdown

**All 12 tasks in the plan are implemented.** The three root-cause bugs from v3 testing are fixed:

- AI errors no longer silently score as off-task → the cycle is skipped, suspicion is never bumped
- Provider unreachability → `.aiUnavailable` pause card + auto-resume via health polling
- 15s settle window at session start / resume suppresses early false prompts
- Off-task prompts only raised during `.session` phase (never from `.progress`, `.complete`, etc.)
- Rich session-completion breakdown: mechanical timeline ranges + per-task AI commentary with local comparison fallback

### Stats
- **223 tests passing** (`make test`, deterministic across repeated runs)
- **Build clean** (`make build`)
- **30 files changed**, 703 insertions, 15 deletions (cf45c86..HEAD)
- No new dependencies, no external config files
- Commits: `c329543` (feature) → `b6e1342` (Phase A/B test backfill) → `06347b6` (wiring fixes) → `9996f93` (critical capture-restart fix + cleanup)

### Phase A — Provider availability (Tasks 1–5)
- `AIService.healthCheck()` protocol method + implementations (Ollama: `GET /api/tags`, OpenRouter: key presence, Claude: stub)
- New `.aiUnavailable` AppPhase + `aiUnavailableHint` on AppState
- Classify errors → skip cycle (never off-task), connection failure → pause capture loop
- Auto-resume: poll `healthCheck()` every 5s, dismiss card + restart on recovery
- Startup health probe in AppDelegate
- AIUnavailableView: angry ball, hint text, spinning ProgressView, "Retrying automatically…"
- FloatingPanel.resize(for:) handles `.aiUnavailable` (300×300 anchor-right)
- RootCoordinatorView: `case .aiUnavailable → AIUnavailableView()`

### Phase B — Grace window + prompt suppression (Tasks 6–7)
- Injectable clock (`now: () -> Date`) for deterministic testability
- 15s settle window (`settleWindow: TimeInterval = 15`)
- `beginSession()`, `resumeAfterExcuse()`, `recoverFromAIUnavailable()` all arm the settle window
- Off-task escalation gated on `!inSettleWindow && state.appPhase == .session`
- Timeline still recorded in all phases; only the prompt is suppressed

### Phase C — Session completion breakdown (Tasks 8–11)
- `TimelineRange { startOffset, endOffset, label, taskIndex? }` model
- `TimelineCoalescer.ranges()` — coalesces contiguous `(taskIndex, label)` groups
- `PerTaskSessionInput`, `SessionRecap`, `PerTaskComment` types (all existed from v3 design)
- `AIService.summarizeSession()` protocol method + Ollama/OpenRouter implementations with JSON schema
- `AIPrompts.sessionSystem`, `buildSessionPrompt()`, `parseSessionComments()` — prompt + parser
- Engine `finalizeSessionRecap()`: builds ranges + local comparisons per task, calls AI for prose, falls back to local comparison string on failure
- CompletionView renders timeline ranges + per-task comment cards

### Phase D — Verification (Task 12)
- `make test` → 223/223 passing (run multiple times, deterministic)
- `make build` → clean

### Multi-agent verification pass (post-implementation)
The initial v3.1 commit (`c329543`) built green but was unreviewed and missing
the Phase A/B test coverage and two wiring steps. A subagent-driven review
closed the gaps:

- **Backfilled the 4 missing Phase A/B TDD suites** (`b6e1342`):
  `EngineErrorHandlingTests` (AI error skips the cycle, never off-task),
  `EngineAvailabilityTests` (pause + resume + `isCapturing` edge contract),
  `EngineSettleWindowTests` (injected clock; in-window suppress / post-window
  prompt), `EnginePromptGateTests` (`.progress` records but never prompts).
  The existing engine logic matched spec — no engine changes were needed to
  pass them.
- **Spec-compliance review found 2 omitted wiring steps** (`06347b6`):
  `finalizeSessionRecap()` was dead code (never called) → wired into the
  AppDelegate phase-watcher on `.complete` with a double-build guard; and the
  Task 4 startup `healthCheck` probe was missing → added so the unavailable
  card shows at launch, not only after the first failed cycle.
- **Code-quality review caught a CRITICAL silent failure** (`9996f93`):
  after AI recovery the screen-capture loop never restarted — `enterAIUnavailable`
  stopped capture but left `state.isCapturing == true`, so the AppDelegate
  edge-watcher saw no `false→true` edge on recovery and never re-called
  `start()`. The app would return to a smiling on-task ball while no longer
  watching the screen. Fixed by clearing `isCapturing` on outage (regression
  test added). Same commit: health-poll thrash guard (sleep before first
  probe), robust per-task comment realignment (model drift can't drop a task's
  card), `enumerated()` task-index fix, hoisted off-task count, named
  `AppConstants.cycleSeconds`, deleted dead `mmss` helpers, consolidated
  triplicated test fake.

Test files: existing v3 suites + `TimelineRangeTests`, `SummarizeSessionParseTests`,
`EngineSessionRecapTests`, plus the 4 backfilled Phase A/B suites and a shared
`TestFakes.swift`.

### Still open / non-code
- **Manual QA not yet run** — checklist in [`v3.1-manual-qa.md`](v3.1-manual-qa.md)
  (needs Screen Recording permission + Ollama running, then repeat with
  `AI_PROVIDER=openrouter`). Includes the critical post-recovery capture-restart
  check.
- **Security:** rotate the leaked v2 OpenRouter key at openrouter.ai (it's in git history before commit `9c4ddf3`)

### Resolved
- **Findings #2/#3 (prompt-tuning) — CLEARED.** The opt-in live suite
  `make test-integration` (see `Tests/IntegrationTests/`) runs the real
  `qwen2.5:7b` and confirms classification + excuse evaluation behave correctly
  with Ollama running (aligned excuse → justified, on/off-task classified
  correctly). 10/10 live tests pass. The earlier symptoms were the v3
  error-mis-scoring root cause, fixed in Phase A.
