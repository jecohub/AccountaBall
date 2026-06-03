# AccountaBall v3 — Design Document
_2026-06-03_

## Overview

v3 turns AccountaBall from a pure accountability widget into a **local task-memory
system**. It keeps the entire v2 pipeline (ScreenCaptureKit → Vision OCR →
`AIService.classifyMulti` → `AccountabilityEngine`) and adds a learning layer on top:

- **Alignment allowances** — when you justify an off-task-looking activity, the AI
  records a task-scoped allowance so it stops re-asking and credits the time correctly.
- **Full session timeline** — every 5s read is stored, building an honest record of how
  a session unfolded.
- **Task recaps** — on completion the AI writes a summary + the steps you took, plus the
  total time, and (on repeats) a faster/slower finding with the reason.
- **Cross-session reuse** — a task you type today is semantically matched to ones you
  finished before, reviving its allowances and know-how (with your confirmation).
- **Local-first AI** — Ollama is the default provider (no API key, fully local);
  OpenRouter remains available, selected explicitly by env var.

All persistence moves to **SwiftData**. Nothing leaves the machine except the AI HTTP
call (and with Ollama as default, even that stays on localhost).

---

## Scope & Key Decisions

| Decision | Choice |
|---|---|
| v3 purpose | Smarter testing on the developer's own machine |
| AI provider | **Ollama default + OpenRouter optional**, chosen by env var, **no auto-fallback** |
| Storage | **SwiftData** (bumps minimum OS to macOS 14 Sonoma) |
| Allowance key | Task-scoped (an allowance counts toward one specific task) |
| Allowance lifetime | Persist across sessions, **confirm once on reuse** |
| Justification logging | **Log every interrogation** (justified or not); create an allowance **only when justified** |
| Steps capture | Recap **for the user** AND learning **for the AI** |
| Timeline granularity | **One entry per 5s cycle** (full resolution) |
| Timeline entry detail | timestamp + task index/off-task + short AI activity label (returned in the same classify call) |
| Task matching | AI semantic match + **user confirms** before linking |
| Recap extras | total time on task; repeat comparison (faster/slower + AI-written reason) |
| Test logs | Full per-cycle trace (OCR truncated, prompt, raw reply, parsed result) + queryable SwiftData |

**Constraint change:** minimum OS moves from macOS 13 (Ventura) to **macOS 14 (Sonoma)**
because SwiftData requires it. CLAUDE.md Key Constraints to be updated; a decision record
to be filed.

---

## Section 1 — Architecture & SwiftData Model

SwiftData owns all structured persistence (UserDefaults retired for tasks). The enriched
debug log stays a flat text file — it's a trace, not queryable data.

Store location: `~/Library/Application Support/AccountaBall/` (SwiftData-managed SQLite).

```swift
@Model final class WorkSession {
    var startedAt: Date
    var endedAt: Date?
    @Relationship(deleteRule: .cascade) var entries: [TimelineEntry] = []
    @Relationship(deleteRule: .cascade) var justifications: [JustificationEvent] = []
    var taskTitles: [String] = []        // snapshot of what was declared
    init(startedAt: Date) { self.startedAt = startedAt }
}

@Model final class TimelineEntry {       // one per 5s cycle
    var at: Date
    var taskIndex: Int?                   // nil = off-task
    var label: String                     // AI's 3-5 word activity label
}

@Model final class JustificationEvent {  // one per off-task interrogation
    var at: Date
    var excuse: String                    // what the user typed
    var justified: Bool
    var inferredTaskIndex: Int?           // which task it was judged against
    var activity: String                  // AI's short description of the activity
}

@Model final class KnowledgeTask {        // persists across sessions
    @Attribute(.unique) var normalizedTitle: String
    var originalTitles: [String] = []     // every phrasing the user has used
    var lastCompletedAt: Date
    var timesCompleted: Int = 0
    @Relationship(deleteRule: .cascade) var allowances: [Allowance] = []
    @Relationship(deleteRule: .cascade) var completions: [TaskCompletion] = []
}

@Model final class Allowance {
    var rule: String                      // AI-written, e.g. "watching React tutorials on YouTube"
    var createdAt: Date
    var needsConfirmation: Bool = false   // true when revived in a new session
}

@Model final class TaskCompletion {       // one record per finished run
    var completedAt: Date
    var duration: TimeInterval            // total time on this task that session
    var summary: String
    var steps: [String]
    var offTaskCount: Int                 // times flagged that run (feeds the "why")
}
```

**Concurrency:** SwiftData `@Model` objects aren't `Sendable`, and the engine runs AI calls
off-main. The engine **snapshots** models into small value structs before each AI call, then
writes results back on the main `ModelContext`. `TaskItem` (v2) stays a plain struct for
AI/UI ergonomics and gains `knowledgeRef: UUID?` linking it to a `KnowledgeTask` on reuse.

---

## Section 2 — Enriched AI Call & Per-Cycle Timeline

`classifyMulti` now returns **classification + a short activity label** in one call.

Model output format (pipe-separated):
```
TASK:2 | editing AppDelegate.swift
OFFTASK | browsing Twitter feed
DONE:0  | proposal doc finalized
```

```swift
enum MultiTaskResult {
    case onTask(index: Int, label: String)
    case offTask(label: String)
    case done(index: Int, label: String)
}
```
`parse(_:)` splits on `|`, trims, and defaults the label to `""` if the model omits it
(older behavior still works).

**Allowances feed the prompt.** Before classifying, the engine gathers active (confirmed)
allowances for the current tasks and appends them to the system prompt:
```
Allowances — treat these as ON-TASK for the named task:
- Task 2: watching React tutorials on YouTube
- Task 0: reading Swift docs
```
This is what makes the AI stop flagging justified activity and credits the time to the
right task.

**Recording loop** (`AccountabilityEngine.start`), each cycle:
1. Capture → OCR (unchanged).
2. `classifyMulti` returns result + label.
3. Append one `TimelineEntry` (timestamp, taskIndex/nil, label) to the current `WorkSession`
   on the main context.
4. `processResult(...)` runs the existing on/off/done logic; per-task timer increments
   stay exactly as v2 (5s per cycle).

Full resolution is preserved on disk; "segments" (e.g. on-task 0–25s, off-task 26–68s) are
**derived** by coalescing same-task entries only when building a recap.

**Storage note:** ~1 MB per 8-hour day — fine for local testing. A retention setting
(e.g. keep raw timeline 30 days, summaries forever) is a *future* knob, not built now.

---

## Section 3 — Alignment Allowances

When flagged off-task (2 consecutive off-task reads → `OFF_TASK` phase, v2 behavior), the
ball asks *"What are you doing?"*. The explanation goes to a richer judge call:

```swift
func evaluateExcuse(excuse:tasks:screenText:) async throws -> ExcuseVerdict

struct ExcuseVerdict {
    let justified: Bool
    let taskIndex: Int?     // which task it supports (AI-inferred)
    let rule: String        // short reusable allowance, AI-written
}
```

Judge output format:
```
JUSTIFIED | 2 | watching React tutorials on YouTube
NOT_JUSTIFIED | |
```

**Capture the whole flow — log everything, allow only the justified:**
- **Every** interrogation writes a `JustificationEvent` (justified or not) to the current
  `WorkSession`. This preserves the full picture for analysis: interruption frequency,
  what the user was doing, hit/miss rate, which tasks attract distraction.
- **Justified** → also create an `Allowance` and attach it to that task's `KnowledgeTask`
  (created on the fly if the task isn't linked yet). Ball smiles, *"Okay, carry on 👍"*,
  slides back. From the next cycle the allowance is injected into the classify prompt, so
  the activity reads as `TASK:N` and time credits correctly.
- **Not justified** → `JustificationEvent` only; no allowance. Ball stays angry,
  *"Get back to work."* (Rejected excuses must never silently excuse future activity.)

**Confirm-on-reuse:** allowances persist with their `KnowledgeTask`. When that task is
reused later, its allowances return with `needsConfirmation = true`. The first time the AI
would apply one, the ball asks once — *"Still counts toward 'Write proposal'? — watching
React tutorials."* Yes → `needsConfirmation = false`, silent for the rest of the session.
No → allowance deleted. This is the only re-ask, and only on revival.

---

## Section 4 — Task Completion → Summary, Steps, Time & Comparison

**Trigger:** task completes via progress-panel checkoff or AI `DONE:N` (v2). On completion,
before the completion animation, the engine fires a one-time summarization call
(async, off-main; the animation does not wait on it).

**Input:** task title + context, plus the coalesced timeline for that task index this
session (deduped consecutive labels, with rough durations). Off-task stretches and justified
detours are included so the steps reflect reality. On a **repeat**, the previous
`TaskCompletion` (duration, steps, offTaskCount) is also passed in for comparison.

**Output:**
```swift
struct TaskRecap {
    let summary: String       // 1-2 sentences: what the task was and how it went
    let steps: [String]       // ordered, deduped meaningful actions
    let duration: TimeInterval
    let comparison: String?   // nil on first completion
}
```

The delta (faster/slower + minutes) is computed **in code** for accuracy; the AI writes only
the *why*, grounded in the two runs' steps and off-task counts. Example comparison:

> *"18 min faster than last time (52→34 min). You went straight to the component instead of
> re-reading the docs, and got flagged off-task once instead of four times."*

**Write-back:** a new `TaskCompletion` is appended to the task's `KnowledgeTask`
(`completedAt`, `duration`, `summary`, `steps`, `offTaskCount`); `lastCompletedAt = now`,
`timesCompleted += 1`; allowances retained.

**Recap screen:** the v2 session-summary gains a per-task expandable section showing
**total time**, summary, steps ("here's how you did it"), and — if a repeat — the comparison
line. Same data feeds the AI on reuse (Section 5).

**Resilience:** if the summarization call fails (offline, model down), the task still
completes normally; `summary`/`steps` stay empty and can be backfilled. Accountability never
blocks on learning.

---

## Section 5 — Task Matching & Reuse

In the SETUP screen, as a task row is filled (debounced after both task + context are typed),
the engine matches against stored `KnowledgeTask`s.

1. **Cheap path first** — exact/near-exact `normalizedTitle` or a prior `originalTitles`
   entry short-circuits (no AI call).
2. **AI path** — otherwise one small call compares the new text against stored
   titles + latest summaries and returns the best candidate id + confidence, or "none".
   (Titles/summaries only — no timelines — so it's cheap.)

**Confirm before linking:** on a confident match the ball offers once —
> *"Looks like 'Write the Q3 proposal' — a task you finished before. Bring back what you
> learned?"* **[Yes] [No, fresh task]**

- **Yes** → `TaskItem.knowledgeRef = <KnowledgeTask.id>`:
  - revives allowances with `needsConfirmation = true` (Section 3),
  - loads the latest `TaskCompletion` for the Section 4 comparison,
  - surfaces prior steps as a hint in setup ("here's how you did this last time").
- **No** → a fresh `KnowledgeTask` is created on completion; nothing revived; the new
  phrasing is still recorded so future matches improve.

**No match** → treated as new, learned on completion.

**Edge case:** if two rows match the same `KnowledgeTask`, only the first links; the second
is treated as new (prevents double-crediting allowances).

---

## Section 6 — Logging & Storage Layout

**SwiftData store:** default container at `~/Library/Application Support/AccountaBall/`
(SQLite). Holds all `@Model` types from Section 1. SwiftData manages migrations.

**Enriched debug log** (the per-cycle trace), flat human-readable file at
`~/Library/Application Support/AccountaBall/logs/accountaball.log` (moved out of `/tmp` so
it survives reboots and sits beside the data). Each cycle writes a block:

```
2026-06-03T10:42:15Z  CYCLE
  ocr:    "AppDelegate.swift — func applicationDidFinish…" (truncated 200 chars)
  prompt: tasks=[0,1,2] allowances=[Task2: React tutorials]
  reply:  "TASK:0 | editing AppDelegate"
  parsed: onTask(0) label="editing AppDelegate"
  state:  suspicion=0 activeTask=0
```

Justification events, allowance creation, task matches, and completion recaps each log their
own labeled block, so the log reads as a session narrative. OCR is truncated; the file is
**append-only with a ~5 MB size cap** (rotate → `.log.1`).

**Two tiers by purpose:**
- **SwiftData** — durable, queryable record (learning, recaps, comparisons).
- **Log file** — disposable debugging trace, regenerated each run.

No data leaves the machine.

---

## Section 7 — AI Provider & Code Changes

**Provider selection** via a factory in `AppDelegate`, defaulting to Ollama:

```swift
let provider = env["AI_PROVIDER"] ?? "ollama"   // "ollama" | "openrouter"
let aiService: AIService = {
    switch provider {
    case "openrouter":
        guard let key = env["OPENROUTER_API_KEY"] else { fatalSetupHint("set OPENROUTER_API_KEY") }
        return OpenRouterAIService(apiKey: key,
                                   model: env["OPENROUTER_MODEL"] ?? "anthropic/claude-haiku-4-5")
    default:
        return OllamaAIService(host: env["OLLAMA_HOST"] ?? "http://localhost:11434",
                               model: env["OLLAMA_MODEL"] ?? "qwen2.5:7b")
    }
}()
```

- **Hardcoded key removed.** OpenRouter only loads its key when explicitly selected.
- **No auto-fallback** — if the chosen provider fails (e.g. Ollama not running), the engine
  logs it and the ball shows a one-line setup hint
  (*"Start Ollama, or run with AI_PROVIDER=openrouter"*). It does not silently switch.
- `OllamaAIService` calls `POST /api/chat` with `format` set to a JSON schema so even a small
  model returns clean structured output for all four jobs (classify+label, excuse verdict,
  summary+steps, task match).
- Recommended models: `qwen2.5:7b` (better nuance) or `llama3.2:3b` (faster loop). We
  classify OCR text, not images, so a text model suffices and stays warm via `keep_alive`.

**Existing code touched:**
- New `OllamaAIService` implementing `AIService`.
- `MultiTaskResult` gains `label`; `parse(_:)` updated (+ tests).
- `AIService` protocol: `evaluateExcuse` → `ExcuseVerdict`; add `summarizeTask(...)` and
  `matchTask(...)`. All services conform (`Ollama`, `OpenRouter`, `Claude`).
- `AccountabilityEngine` — inject `ModelContext`; per-cycle `TimelineEntry`;
  justification/allowance writes; completion summarization; setup-time matching;
  model→struct snapshots before AI calls.
- `AppDelegate` — `ModelContainer` setup + provider factory; remove hardcoded key.
- `TaskItem` gains `knowledgeRef: UUID?`.
- Views — `TaskSetupView` (match prompt + "how you did it" hint),
  `AccountaProgressView`/`CompletionView` (per-task recap with time + comparison).

**Docs:** CLAUDE.md updated (macOS 14, SwiftData, Ollama default + OpenRouter optional, env
vars); decision records for the macOS bump and the Ollama-default choice.

---

## Testing

New unit tests:
- label parsing in `MultiTaskResult.parse`
- `ExcuseVerdict` parsing (justified / not-justified / malformed)
- allowance injection into the classify prompt
- segment coalescing used for recaps
- task-match cheap-path short-circuit logic
- `JustificationEvent` written on both verdicts; `Allowance` only on justified

Human visual QA still required for the new recap UI (time, steps, comparison line) and the
match-confirm / allowance-confirm prompts.

---

## What Stays the Same

- `ScreenCaptureService`, `OCRService`, `NotificationService` — unchanged.
- Off-task trigger rule (2 consecutive off-task reads), creep animation, completion 3-point
  shot — unchanged.
- `Makefile` build system — unchanged (env vars gain `AI_PROVIDER`, `OLLAMA_MODEL`).
- All v2 visual flows (welcome bounce, setup table, session edge ball, progress panel,
  completion) — preserved; recap UI is additive.
