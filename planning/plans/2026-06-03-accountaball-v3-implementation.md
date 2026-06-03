# AccountaBall v3 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Turn AccountaBall into a local task-memory system — alignment allowances, a full per-cycle session timeline, AI task recaps (summary/steps/time/repeat-comparison), cross-session task reuse, SwiftData persistence, and Ollama as the default AI provider.

**Architecture:** Keep the v2 pipeline (ScreenCaptureKit → Vision OCR → `AIService.classifyMulti` → `AccountabilityEngine`). Add a SwiftData persistence layer plus four AI jobs (classify+label, excuse verdict, task summary, task match). The engine snapshots non-`Sendable` `@Model` objects into value structs before each off-main AI call. Build pure logic first (TDD), then SwiftData models, then services, then engine wiring, then UI.

**Tech Stack:** Swift 6 / SwiftUI, SwiftData (macOS 14+), custom `swiftc` Makefile, custom test harness (`expect`/`suite` in `Tests/MicroTest.swift`, registered in `Tests/TestRunner/main.swift`), Ollama (`localhost:11434`) + OpenRouter.

---

## Conventions for the executor (read once)

- **All code lives in the `src/` repo, which is its own git repo.** Run every build/test/commit from inside `src/`: `cd src && make test`. Commits are made inside `src/` (`cd src && git add … && git commit …`). The outer repo only holds planning/docs — do not commit code there.
- **Build/test commands:**
  - Run all tests: `cd src && make test` (builds the lib + runner, runs it; exit 0 = pass).
  - Build app: `cd src && make build`. Run app: `cd src && make run`.
- **Adding a test:** create `Tests/AccountaBallTests/<Name>Tests.swift` exposing `func run<Name>Tests()`, then register it in `Tests/TestRunner/main.swift` (add a call inside the `DispatchQueue.main.async` block, before `reportAndExit()`). Use `suite("…") { expect(cond, "msg") }`.
- **Test files import with** `@testable import AccountaBall`.
- **SwiftData tests** use an in-memory container: `ModelConfiguration(isStoredInMemoryOnly: true)`. The harness runs on the main queue, satisfying `@MainActor`.
- **YAGNI/DRY/TDD, commit after every task.** Use Conventional Commit messages (`feat:`, `refactor:`, `test:`, `docs:`).
- **Pre-work security task:** the OpenRouter key currently in `Sources/AccountaBall/AppDelegate.swift:21` is in `src/` git history. Revoke/rotate it at openrouter.ai before/while doing Task 13. The code reference is removed in this plan; rotating invalidates the leaked secret.

---

## Phase A — Pure logic (TDD)

### Task 1: Add activity label to `MultiTaskResult`

**Files:**
- Modify: `Sources/AccountaBall/Models/MultiTaskResult.swift`
- Modify (tests): `Tests/AccountaBallTests/MultiTaskResultTests.swift`

**Step 1: Update the failing tests first.** Replace the body of `runMultiTaskResultTests()` so cases carry a label and `parse` reads the `RESULT | label` format:

```swift
@testable import AccountaBall

func runMultiTaskResultTests() {
    suite("MultiTaskResultTests") {
        expect(MultiTaskResult.parse("TASK:0 | editing AppDelegate") == .onTask(index: 0, label: "editing AppDelegate"), "parses TASK with label")
        expect(MultiTaskResult.parse("OFFTASK | browsing twitter") == .offTask(label: "browsing twitter"), "parses OFFTASK with label")
        expect(MultiTaskResult.parse("DONE:2 | proposal finalized") == .done(index: 2, label: "proposal finalized"), "parses DONE with label")
        expect(MultiTaskResult.parse("TASK:3") == .onTask(index: 3, label: ""), "missing label defaults to empty")
        expect(MultiTaskResult.parse("  task:1 | Foo \n") == .onTask(index: 1, label: "Foo"), "trims + lowercases keyword, preserves label case")
        expect(MultiTaskResult.parse("garbage") == .offTask(label: ""), "unknown defaults to offTask")
        expect(MultiTaskResult.parse("") == .offTask(label: ""), "empty defaults to offTask")
        expect(MultiTaskResult.onTask(index: 0, label: "a") != MultiTaskResult.onTask(index: 1, label: "a"), "different indices differ")
        expect(MultiTaskResult.onTask(index: 0, label: "a") != MultiTaskResult.onTask(index: 0, label: "b"), "different labels differ")
        expect(MultiTaskResult.done(index: 0, label: "x") != .offTask(label: "x"), "done != offTask")
    }
}
```

**Step 2: Run to confirm failure.** `cd src && make test` → Expected: compile error / FAIL (cases lack `label`).

**Step 3: Implement.** Replace `MultiTaskResult.swift`:

```swift
enum MultiTaskResult: Equatable {
    case onTask(index: Int, label: String)
    case offTask(label: String)
    case done(index: Int, label: String)

    /// Accepts "RESULT | label". Label is optional. Keyword is case-insensitive;
    /// label case is preserved.
    static func parse(_ raw: String) -> MultiTaskResult {
        let parts = raw.split(separator: "|", maxSplits: 1, omittingEmptySubsequences: false)
        let keyword = parts.first.map { String($0).trimmingCharacters(in: .whitespacesAndNewlines).uppercased() } ?? ""
        let label = parts.count > 1 ? String(parts[1]).trimmingCharacters(in: .whitespacesAndNewlines) : ""
        if keyword == "OFFTASK" { return .offTask(label: label) }
        if keyword.hasPrefix("TASK:"), let idx = Int(keyword.dropFirst(5)) { return .onTask(index: idx, label: label) }
        if keyword.hasPrefix("DONE:"), let idx = Int(keyword.dropFirst(5)) { return .done(index: idx, label: label) }
        return .offTask(label: label)
    }
}
```

**Step 4: Run tests.** `cd src && make test` → some other files will now fail to compile (engine/services pattern-match the old cases). That's expected; Tasks 8–9 & 12 fix them. To keep this task green in isolation, also apply the minimal compile fixes below now, then re-run.

**Step 4a: Minimal compile fixes (so the suite builds):**
- `Engine/AccountabilityEngine.swift` `processResult` switch: change `case .onTask(let index):` → `case .onTask(let index, _):`, `case .offTask:` → `case .offTask:` stays but pattern is now `.offTask(_)` → use `case .offTask:` won't match; use `case .offTask(_):`. And `case .done(let index):` → `case .done(let index, _):`. (Label handling added properly in Task 12 — for now discard with `_`.)
- `Services/OpenRouterAIService.swift` `classifyMulti`: `MultiTaskResult.parse(raw)` still compiles (returns labeled cases). OK.
- `Services/ClaudeAIService.swift` `classifyMulti`: change `return .onTask(index: 0)` → `.onTask(index: 0, label: "")`, `.offTask` → `.offTask(label: "")`, `.done(index: 0)` → `.done(index: 0, label: "")`.
- `Tests/AccountaBallTests/MultiTaskClassificationTests.swift`: update any `== .onTask(index: …)` / `.offTask` / `.done(index: …)` comparisons to include `label:`/`label: ""` (read the file, fix each `expect`).

**Step 5: Run tests green.** `cd src && make test` → Expected: PASS (all suites).

**Step 6: Commit.** `cd src && git add -A && git commit -m "feat: add activity label to MultiTaskResult"`

---

### Task 2: `ExcuseVerdict` type + parser

**Files:**
- Create: `Sources/AccountaBall/Models/ExcuseVerdict.swift`
- Create (test): `Tests/AccountaBallTests/ExcuseVerdictTests.swift`
- Modify: `Tests/TestRunner/main.swift` (register `runExcuseVerdictTests()`)

**Step 1: Failing test.**

```swift
@testable import AccountaBall

func runExcuseVerdictTests() {
    suite("ExcuseVerdictTests") {
        let j = ExcuseVerdict.parse("JUSTIFIED | 2 | watching React tutorials")
        expect(j.justified == true, "justified flag")
        expect(j.taskIndex == 2, "task index parsed")
        expect(j.rule == "watching React tutorials", "rule parsed")

        let n = ExcuseVerdict.parse("NOT_JUSTIFIED | |")
        expect(n.justified == false, "not justified")
        expect(n.taskIndex == nil, "no task index")
        expect(n.rule == "", "empty rule")

        // "NOT_JUSTIFIED" contains "JUSTIFIED" — must not false-positive.
        expect(ExcuseVerdict.parse("not justified").justified == false, "substring guard")
        expect(ExcuseVerdict.parse("garbage").justified == false, "unknown defaults not justified")
        expect(ExcuseVerdict.parse("JUSTIFIED").taskIndex == nil, "missing index tolerated")
    }
}
```

**Step 2: Register + run to fail.** Add `runExcuseVerdictTests()` to `TestRunner/main.swift`. `cd src && make test` → FAIL (type missing).

**Step 3: Implement `ExcuseVerdict.swift`.**

```swift
struct ExcuseVerdict: Equatable {
    let justified: Bool
    let taskIndex: Int?
    let rule: String

    /// Accepts "VERDICT | taskIndex | rule". Reject NOT before matching JUSTIFIED.
    static func parse(_ raw: String) -> ExcuseVerdict {
        let parts = raw.split(separator: "|", omittingEmptySubsequences: false).map {
            $0.trimmingCharacters(in: .whitespacesAndNewlines)
        }
        let verdict = (parts.first ?? "").uppercased()
        let justified = !verdict.contains("NOT") && verdict.contains("JUSTIFIED")
        let taskIndex = parts.count > 1 ? Int(parts[1]) : nil
        let rule = parts.count > 2 ? parts[2] : ""
        return ExcuseVerdict(justified: justified, taskIndex: justified ? taskIndex : nil, rule: justified ? rule : "")
    }
}
```

**Step 4: Run.** `cd src && make test` → PASS.
**Step 5: Commit.** `cd src && git add -A && git commit -m "feat: add ExcuseVerdict type and parser"`

---

### Task 3: Timeline coalescing helper (for recaps)

**Files:**
- Create: `Sources/AccountaBall/Util/TimelineCoalescer.swift`
- Create (test): `Tests/AccountaBallTests/TimelineCoalescerTests.swift`
- Modify: `Tests/TestRunner/main.swift`

**Step 1: Failing test.** Coalesce a sequence of `(taskIndex, label)` reads into per-task ordered, deduped labels.

```swift
@testable import AccountaBall

func runTimelineCoalescerTests() {
    suite("TimelineCoalescerTests") {
        let reads: [(Int?, String)] = [
            (0, "editing AppDelegate"), (0, "editing AppDelegate"), (0, "running tests"),
            (nil, "twitter"), (0, "editing Engine")
        ]
        let steps = TimelineCoalescer.labelsForTask(index: 0, reads: reads)
        expect(steps == ["editing AppDelegate", "running tests", "editing Engine"],
               "dedupes consecutive, keeps order, skips off-task")

        let cycles = TimelineCoalescer.cycleCountForTask(index: 0, reads: reads)
        expect(cycles == 4, "counts on-task reads for the index")
    }
}
```

**Step 2: Register + run to fail.**
**Step 3: Implement.**

```swift
enum TimelineCoalescer {
    /// Ordered, consecutive-deduped labels for one task index. Off-task reads are skipped
    /// but DO break a run (so a label can repeat after an interruption).
    static func labelsForTask(index: Int, reads: [(Int?, String)]) -> [String] {
        var out: [String] = []
        for (idx, label) in reads where idx == index {
            if out.last != label, !label.isEmpty { out.append(label) }
        }
        return out
    }

    static func cycleCountForTask(index: Int, reads: [(Int?, String)]) -> Int {
        reads.filter { $0.0 == index }.count
    }
}
```

**Step 4: Run → PASS. Step 5: Commit** `feat: add TimelineCoalescer for recap label extraction`.

---

### Task 4: Task-match cheap-path + title normalization

**Files:**
- Create: `Sources/AccountaBall/Util/TaskMatcher.swift`
- Create (test): `Tests/AccountaBallTests/TaskMatcherTests.swift`
- Modify: `Tests/TestRunner/main.swift`

**Step 1: Failing test.**

```swift
@testable import AccountaBall

func runTaskMatcherTests() {
    suite("TaskMatcherTests") {
        expect(TaskMatcher.normalize("  Write the Q3 Proposal!! ") == "write the q3 proposal",
               "lowercases, trims, strips trailing punctuation, collapses spaces")
        // cheap match: exact normalized title
        let candidates = [(id: "A", normalized: "write proposal", originals: ["Write proposal"]),
                          (id: "B", normalized: "review slides", originals: ["Review slides"])]
        expect(TaskMatcher.cheapMatch("write proposal", in: candidates) == "A", "exact normalized hit")
        // cheap match via a prior original phrasing (normalized)
        expect(TaskMatcher.cheapMatch("review slides", in: candidates) == "B", "matches by normalized original")
        expect(TaskMatcher.cheapMatch("unrelated thing", in: candidates) == nil, "no cheap hit returns nil")
    }
}
```

**Step 2: Register + run to fail.**
**Step 3: Implement.**

```swift
import Foundation

enum TaskMatcher {
    static func normalize(_ s: String) -> String {
        let lowered = s.lowercased().trimmingCharacters(in: .whitespacesAndNewlines)
        let stripped = lowered.trimmingCharacters(in: CharacterSet.punctuationCharacters)
        let collapsed = stripped.split(whereSeparator: { $0 == " " }).joined(separator: " ")
        return collapsed
    }

    /// Returns the candidate id whose normalized title (or any normalized original) equals
    /// the normalized query. nil if none — caller then tries the AI semantic match.
    static func cheapMatch(_ query: String,
                           in candidates: [(id: String, normalized: String, originals: [String])]) -> String? {
        let q = normalize(query)
        for c in candidates {
            if c.normalized == q { return c.id }
            if c.originals.map(normalize).contains(q) { return c.id }
        }
        return nil
    }
}
```

**Step 4: Run → PASS. Step 5: Commit** `feat: add TaskMatcher normalization + cheap-path matching`.

---

### Task 5: Duration delta computation

**Files:**
- Create: `Sources/AccountaBall/Util/DurationDelta.swift`
- Create (test): `Tests/AccountaBallTests/DurationDeltaTests.swift`
- Modify: `Tests/TestRunner/main.swift`

**Step 1: Failing test.**

```swift
@testable import AccountaBall

func runDurationDeltaTests() {
    suite("DurationDeltaTests") {
        let faster = DurationDelta.compare(current: 34*60, previous: 52*60)
        expect(faster.fasterThanPrevious == true, "34<52 is faster")
        expect(faster.deltaMinutes == 18, "18 minute delta")
        let slower = DurationDelta.compare(current: 60*60, previous: 50*60)
        expect(slower.fasterThanPrevious == false, "slower")
        expect(slower.deltaMinutes == 10, "abs delta minutes")
    }
}
```

**Step 2: Register + run to fail.**
**Step 3: Implement.**

```swift
import Foundation

struct DurationDelta {
    let fasterThanPrevious: Bool
    let deltaMinutes: Int

    static func compare(current: TimeInterval, previous: TimeInterval) -> DurationDelta {
        let deltaSec = abs(current - previous)
        return DurationDelta(fasterThanPrevious: current < previous,
                             deltaMinutes: Int((deltaSec / 60).rounded()))
    }
}
```

**Step 4: Run → PASS. Step 5: Commit** `feat: add DurationDelta for repeat-task comparison`.

---

## Phase B — SwiftData models

### Task 6: Define `@Model` classes + in-memory smoke test

**Files:**
- Create: `Sources/AccountaBall/Models/Persistence.swift` (all `@Model` types + a `ModelContainer` factory)
- Create (test): `Tests/AccountaBallTests/PersistenceTests.swift`
- Modify: `Tests/TestRunner/main.swift`

**Step 1: Failing test** (in-memory container; insert + fetch + cascade):

```swift
import SwiftData
@testable import AccountaBall

@MainActor
func runPersistenceTests() {
    suite("PersistenceTests") {
        guard let container = try? AccountaBallStore.makeContainer(inMemory: true) else {
            expect(false, "container builds"); return
        }
        let ctx = container.mainContext
        let kt = KnowledgeTask(normalizedTitle: "write proposal", lastCompletedAt: .now)
        kt.allowances.append(Allowance(rule: "react tutorials", createdAt: .now))
        kt.completions.append(TaskCompletion(completedAt: .now, duration: 600, summary: "s", steps: ["a"], offTaskCount: 1))
        ctx.insert(kt)
        try? ctx.save()

        let fetched = (try? ctx.fetch(FetchDescriptor<KnowledgeTask>())) ?? []
        expect(fetched.count == 1, "one knowledge task persisted")
        expect(fetched.first?.allowances.count == 1, "allowance related")
        expect(fetched.first?.completions.first?.steps == ["a"], "completion steps round-trip")
    }
}
```

**Step 2: Register** (call `runPersistenceTests()`), run → FAIL (types missing).

**Step 3: Implement `Persistence.swift`** — the six `@Model` types exactly as in the design doc Section 1 (`WorkSession`, `TimelineEntry`, `JustificationEvent`, `KnowledgeTask`, `Allowance`, `TaskCompletion`), each with the listed properties, `@Relationship(deleteRule: .cascade)` where shown, and memberwise `init`s. Add:

```swift
import SwiftData
import Foundation

enum AccountaBallStore {
    static func makeContainer(inMemory: Bool = false) throws -> ModelContainer {
        let schema = Schema([WorkSession.self, TimelineEntry.self, JustificationEvent.self,
                             KnowledgeTask.self, Allowance.self, TaskCompletion.self])
        let config = ModelConfiguration(schema: schema, isStoredInMemoryOnly: inMemory)
        return try ModelContainer(for: schema, configurations: [config])
    }
}
```

> Note: on-disk container defaults to Application Support automatically; we only override for tests.

**Step 4: Run → PASS. Step 5: Commit** `feat: add SwiftData models + container factory`.

---

### Task 7: Snapshot value structs (model → Sendable)

**Files:**
- Create: `Sources/AccountaBall/Models/Snapshots.swift`
- Create (test): `Tests/AccountaBallTests/SnapshotTests.swift`
- Modify: `Tests/TestRunner/main.swift`

**Why:** `@Model` objects aren't `Sendable`; the engine must convert to plain structs before off-main AI calls.

**Step 1: Failing test** — build a `KnowledgeTaskSnapshot` from a `KnowledgeTask` and assert fields/active-allowance filtering:

```swift
@testable import AccountaBall
import SwiftData

@MainActor
func runSnapshotTests() {
    suite("SnapshotTests") {
        let kt = KnowledgeTask(normalizedTitle: "t", lastCompletedAt: .now)
        kt.allowances = [Allowance(rule: "ok", createdAt: .now),                       // active
                         Allowance(rule: "pending", createdAt: .now, needsConfirmation: true)] // not yet active
        let snap = KnowledgeTaskSnapshot(kt)
        expect(snap.normalizedTitle == "t", "title copied")
        expect(snap.activeAllowanceRules == ["ok"], "only confirmed allowances are active")
    }
}
```

**Step 2: Register + run to fail.**
**Step 3: Implement** `Snapshots.swift`:

```swift
struct KnowledgeTaskSnapshot: Sendable {
    let normalizedTitle: String
    let summaryOfLatest: String
    let activeAllowanceRules: [String]   // confirmed only

    init(_ kt: KnowledgeTask) {
        normalizedTitle = kt.normalizedTitle
        summaryOfLatest = kt.completions.sorted { $0.completedAt > $1.completedAt }.first?.summary ?? ""
        activeAllowanceRules = kt.allowances.filter { !$0.needsConfirmation }.map { $0.rule }
    }
}
```

**Step 4: Run → PASS. Step 5: Commit** `feat: add Sendable snapshot structs for AI calls`.

---

## Phase C — AI service protocol + providers

### Task 8: Extend `AIService` protocol; make all conformers compile

**Files:**
- Modify: `Sources/AccountaBall/Services/AIService.swift`
- Modify: `Sources/AccountaBall/Services/OpenRouterAIService.swift`
- Modify: `Sources/AccountaBall/Services/ClaudeAIService.swift`

**Step 1: Update the protocol.**

```swift
protocol AIService {
    func classify(task: String, screenText: String) async throws -> BallState
    func classifyMulti(tasks: [TaskItem], screenText: String, allowanceRulesByIndex: [Int: [String]]) async throws -> MultiTaskResult
    func evaluateExcuse(excuse: String, tasks: [TaskItem], screenText: String) async throws -> ExcuseVerdict
    func summarizeTask(title: String, context: String, steps: [String], durationSeconds: TimeInterval,
                       previous: (durationSeconds: TimeInterval, steps: [String], offTaskCount: Int)?) async throws -> TaskRecap
    func matchTask(query: String, candidates: [(id: String, title: String, summary: String)]) async throws -> (id: String, confident: Bool)?
}
```

Add `TaskRecap` to `Models/` (new file `Sources/AccountaBall/Models/TaskRecap.swift`):

```swift
import Foundation
struct TaskRecap: Equatable {
    let summary: String
    let steps: [String]
    let duration: TimeInterval
    let comparison: String?   // nil on first completion
}
```

**Step 2: Update conformers to compile (real logic for OpenRouter in Task 9; stubs elsewhere).**
- `ClaudeAIService`: update `classifyMulti` signature (ignore `allowanceRulesByIndex`), return labeled cases; change `evaluateExcuse` to return `ExcuseVerdict(justified: true, taskIndex: nil, rule: "")`; add stub `summarizeTask` returning `TaskRecap(summary:"", steps:[], duration: durationSeconds, comparison: nil)` and `matchTask` returning `nil`. (Claude is a legacy/secondary provider; full parity not required for v3.)
- `OpenRouterAIService`: update `classifyMulti` signature; keep returning `MultiTaskResult.parse(raw)`. Temporarily make `evaluateExcuse` return `ExcuseVerdict.parse(raw)` (full prompt in Task 9), and add minimal `summarizeTask`/`matchTask` stubs to compile.

**Step 3: Run** `cd src && make test` → PASS (existing tests still green; signatures align). Fix any remaining call sites the compiler flags (engine call to `classifyMulti` updated in Task 12 — for now pass `allowanceRulesByIndex: [:]` at the existing call site so it compiles).

**Step 4: Commit** `refactor: extend AIService protocol for v3 (label, verdict, summary, match)`.

---

### Task 9: OpenRouter prompt builders + parsers (TDD the pure parts)

**Files:**
- Modify: `Sources/AccountaBall/Services/OpenRouterAIService.swift`
- Create (test): `Tests/AccountaBallTests/OpenRouterPromptTests.swift`
- Modify: `Tests/TestRunner/main.swift`

> Network calls aren't unit-tested; we TDD the static prompt builders, then wire them into the `sendMessage` flow.

**Step 1: Failing test** for prompt builders (make them `static`):

```swift
@testable import AccountaBall

func runOpenRouterPromptTests() {
    suite("OpenRouterPromptTests") {
        let tasks = [TaskItem(task: "Write proposal", context: "Q3"),
                     TaskItem(task: "Review slides", context: "deck")]
        let p = OpenRouterAIService.buildClassifyPrompt(tasks: tasks, screenText: "doc",
                    allowanceRulesByIndex: [1: ["watching tutorials"]])
        expect(p.contains("[0] Write proposal"), "lists task 0")
        expect(p.contains("Allowances"), "includes allowance section when present")
        expect(p.contains("Task 1: watching tutorials"), "allowance scoped to task index")

        let p2 = OpenRouterAIService.buildClassifyPrompt(tasks: tasks, screenText: "doc", allowanceRulesByIndex: [:])
        expect(!p2.contains("Allowances"), "omits allowance section when none")
    }
}
```

**Step 2: Register + run to fail.**

**Step 3: Implement.** Update `buildClassifyPrompt` to take `allowanceRulesByIndex: [Int: [String]]` and append an allowance section only when non-empty. Update the classify **system** prompt to require `RESULT | label` output (label = 3–5 words describing the screen activity). Wire:
- `classifyMulti(...)` builds the user prompt with allowances, sends, returns `MultiTaskResult.parse(raw)`.
- `evaluateExcuse(...)` system prompt asks for `JUSTIFIED | taskIndex | short reusable rule` or `NOT_JUSTIFIED | |`; returns `ExcuseVerdict.parse(raw)`.
- `summarizeTask(...)` system prompt asks for JSON `{"summary": "...", "steps": ["..."], "comparison": "..."}`; request `response_format` json; parse; compute the delta line in code using `DurationDelta` and prepend/format `comparison` (pass `nil` if `previous == nil`). Keep the AI's "why" text.
- `matchTask(...)` system prompt: given query + numbered candidates (title + summary), return `MATCH:<id>|<HIGH|LOW>` or `NONE`; parse to `(id, confident)`.

**Step 4: Run** `cd src && make test` → PASS (prompt-builder tests). **Step 5: Commit** `feat: OpenRouter prompts/parsers for label, excuse, summary, match`.

---

### Task 10: `OllamaAIService` (default provider)

**Files:**
- Create: `Sources/AccountaBall/Services/OllamaAIService.swift`
- Create (test): `Tests/AccountaBallTests/OllamaServiceTests.swift`
- Modify: `Tests/TestRunner/main.swift`

**Step 1: Failing test** for the request-body builder + JSON-schema `format` (pure):

```swift
@testable import AccountaBall
import Foundation

func runOllamaServiceTests() {
    suite("OllamaServiceTests") {
        let body = OllamaAIService.chatBody(model: "qwen2.5:7b", system: "S", user: "U", jsonSchema: ["type": "object"])
        expect(body["model"] as? String == "qwen2.5:7b", "model set")
        expect(body["stream"] as? Bool == false, "non-streaming")
        expect(body["format"] != nil, "format schema attached")
        let msgs = body["messages"] as? [[String: String]]
        expect(msgs?.first?["role"] == "system", "system message first")
        expect(msgs?.last?["content"] == "U", "user content last")
    }
}
```

**Step 2: Register + run to fail.**

**Step 3: Implement `OllamaAIService`** conforming to `AIService`:
- `init(host: String, model: String)`; endpoint `\(host)/api/chat`.
- `static func chatBody(model:system:user:jsonSchema:) -> [String: Any]` building `{model, stream:false, format:<schema>, keep_alive:"10m", messages:[system,user]}`.
- A private `send(system:user:schema:) async throws -> String` that POSTs, decodes `{ "message": { "content": "<json string>" } }`, returns content. 20s timeout. Reuse the same four prompts as OpenRouter (extract the shared prompt strings into a small `AIPrompts` enum in a new file to keep DRY — refactor OpenRouter to use it too).
- Each AIService method: build the matching JSON schema (e.g. classify → `{type:object, properties:{result:{type:string}, label:{type:string}}, required:[result,label]}`), call `send`, decode JSON, map to the domain type (reuse `MultiTaskResult.parse`/`ExcuseVerdict.parse` by formatting decoded fields back into the `RESULT | label` string, or parse JSON directly — prefer parsing JSON directly here since `format` guarantees structure).

**Step 4: Run** `cd src && make test` → PASS (body-builder test). Network paths are exercised in manual QA (Task 19).
**Step 5: Commit** `feat: add OllamaAIService (default local provider)`.

---

## Phase D — Engine + AppDelegate integration

### Task 11: Provider factory + remove hardcoded key

**Files:**
- Modify: `Sources/AccountaBall/AppDelegate.swift`

**Step 1:** Replace the `aiService` construction (lines ~19–23) with the factory from design Section 7 (`AI_PROVIDER` env, default `ollama`; `OLLAMA_HOST`/`OLLAMA_MODEL`; `OPENROUTER_API_KEY`/`OPENROUTER_MODEL`). Remove the literal key string. If `openrouter` selected without a key, call `dbg("FATAL: OPENROUTER_API_KEY not set")` and surface a one-line hint via the panel (set a `state.setupHint` string the welcome view can show — add the property in this task).

**Step 2:** `cd src && make build` → Expected: builds clean.
**Step 3:** Smoke run: `cd src && AI_PROVIDER=ollama make run` (then quit) → launches without crash.
**Step 4: Commit** `feat: env-based AI provider factory; remove hardcoded key`.

> Reminder: revoke the old OpenRouter key at openrouter.ai now — it remains in git history.

---

### Task 12: Engine — `ModelContext`, `WorkSession`, per-cycle `TimelineEntry`

**Files:**
- Modify: `Sources/AccountaBall/Engine/AccountabilityEngine.swift`
- Modify: `Sources/AccountaBall/AppDelegate.swift` (build container, pass `mainContext`)
- Modify (test): `Tests/AccountaBallTests/AccountabilityEngineTests.swift`

**Step 1:** Give the engine an optional `modelContext: ModelContext?` (optional so existing tests that build the engine without SwiftData still pass) and a `currentSession: WorkSession?`. Add `func beginSession()` (insert a `WorkSession(startedAt: .now)`, snapshot task titles) and `func endSession()` (set `endedAt`, save).

**Step 2:** In the capture closure, after classification, build a value tuple and call a new `@MainActor func record(taskIndex: Int?, label: String)` that appends a `TimelineEntry` to `currentSession` and saves. Update `classifyMulti` call to pass `allowanceRulesByIndex` (built from linked KnowledgeTasks — see Task 13; for now pass `[:]`). Update `processResult` to accept labels (`.onTask(index, _)` etc.) and store the active label on `state` for the session view.

**Step 3:** Add a focused test: construct an in-memory container, make an engine with that context, call `beginSession()` then `record(taskIndex: 0, label: "x")` twice, assert `currentSession?.entries.count == 2` and labels stored. Register the test.

**Step 4:** `cd src && make test` → PASS. `make build` → clean.
**Step 5: Commit** `feat: engine records per-cycle timeline into a WorkSession`.

---

### Task 13: Engine — allowances + justification events

**Files:**
- Modify: `Sources/AccountaBall/Engine/AccountabilityEngine.swift`
- Modify: the off-task resolution path (where `evaluateExcuse` is currently called — likely `OffTaskView`/`RootCoordinatorView`; grep `evaluateExcuse`).
- Modify (test): engine tests.

**Step 1:** Add `@MainActor func handleExcuse(_ text: String, tasks: [TaskItem], screenText: String) async`:
- Call `aiService.evaluateExcuse(...)` → `ExcuseVerdict`.
- Always insert a `JustificationEvent(at:.now, excuse:text, justified:v.justified, inferredTaskIndex:v.taskIndex, activity:lastActivityLabel)` into `currentSession`.
- If `v.justified`, resolve the task's `KnowledgeTask` (create+link if needed) and append `Allowance(rule: v.rule, createdAt:.now)`; then `resumeAfterExcuse()`. Else keep angry state (existing behavior).
- Build `allowanceRulesByIndex` from linked KnowledgeTasks' confirmed allowances; store it so the capture loop passes it into `classifyMulti`.

**Step 2:** Update the view call site to call `engine.handleExcuse(...)` instead of the old `evaluateExcuse` bool path.

**Step 3:** Test (in-memory ctx): justified excuse → one `JustificationEvent(justified:true)` + one `Allowance`; not-justified → one `JustificationEvent(justified:false)` + zero allowances. Register.

**Step 4:** `cd src && make test` → PASS. `make build` → clean.
**Step 5: Commit** `feat: log every justification; create allowances only when justified`.

---

### Task 14: Engine — completion summarization + comparison write-back

**Files:**
- Modify: `Sources/AccountaBall/Engine/AccountabilityEngine.swift`
- Modify (test): engine tests.

**Step 1:** On completion (extend `completeTaskAt`/the `.done` path and the progress-panel checkoff path), add `@MainActor func summarizeCompletion(taskIndex: Int) async`:
- Gather this session's reads for the index from `currentSession.entries` → `TimelineCoalescer.labelsForTask`.
- Resolve/create the `KnowledgeTask`; find `previous` = latest existing `TaskCompletion` (if linked/reused).
- Call `aiService.summarizeTask(... previous: ...)` off-main (don't block the completion animation — fire in a `Task`).
- On return, append a new `TaskCompletion` (duration from `state.tasks[index].timeOnTask`, summary, steps, offTaskCount from counting this session's justifications/offtask), bump `timesCompleted`, set `lastCompletedAt`, save. Store the `TaskRecap` on `state` so the recap UI (Task 18) can show it.
- On throw: log, write a `TaskCompletion` with empty summary/steps (don't block).

**Step 2:** Test with a stub `AIService` (create a `FakeAIService` in tests returning a fixed `TaskRecap`): completing task 0 appends a `TaskCompletion` with the recap's steps. Register.

**Step 3:** `cd src && make test` → PASS. `make build` → clean.
**Step 4: Commit** `feat: AI task summary + comparison written to KnowledgeTask on completion`.

---

### Task 15: Engine/setup — task matching with confirm

**Files:**
- Modify: `Sources/AccountaBall/Engine/AccountabilityEngine.swift` (or a new `TaskMatchCoordinator`)
- Modify (test): matcher integration test.

**Step 1:** Add `@MainActor func proposeMatch(for taskText: String) async -> KnowledgeTask?`:
- Fetch all `KnowledgeTask`s; build candidates; try `TaskMatcher.cheapMatch` first.
- If no cheap hit, call `aiService.matchTask(...)`; only return a candidate when `confident == true`.
- Returns the matched `KnowledgeTask` (or nil). Linking + allowance revival happens when the user confirms (Task 16 UI): on confirm, set `TaskItem.knowledgeRef`, set each allowance `needsConfirmation = true`, save.

**Step 2:** Add `TaskItem.knowledgeRef: UUID?` (modify `Models/TaskItem.swift`; update its `Codable`/tests if needed — read the file first).

**Step 3:** Test: with a stored KnowledgeTask "write proposal", `proposeMatch(for: "Write proposal")` returns it via cheap path (use a Fake `matchTask` that would fail, proving cheap path wins). Register.

**Step 4:** `cd src && make test` → PASS. `make build` → clean.
**Step 5: Commit** `feat: cross-session task matching (cheap path + AI fallback)`.

---

## Phase E — Logging

### Task 16: Enriched per-cycle debug log + rotation

**Files:**
- Modify: `Sources/AccountaBall/Util/DebugLog.swift`
- Modify: `Sources/AccountaBall/Engine/AccountabilityEngine.swift` (emit the CYCLE block)
- Create (test): `Tests/AccountaBallTests/DebugLogTests.swift`

**Step 1:** Move the log path to Application Support (`~/Library/Application Support/AccountaBall/logs/accountaball.log`); create the dir if missing. Add a size-cap check: if file > ~5 MB, rename to `.log.1` before appending. Add `func dbgBlock(_ title: String, _ lines: [String])` that writes a timestamped multi-line block (truncate any value to ~200 chars via a helper `truncate(_:max:)`).

**Step 2:** Test the pure helper: `DebugLog.truncate("xxxxx", max: 3) == "xxx…"` and rotation decision `DebugLog.shouldRotate(sizeBytes: 6_000_000) == true`. Register.

**Step 3:** In the engine capture loop, after parse, emit a `CYCLE` block (ocr truncated, prompt summary, raw reply, parsed result, state). Emit labeled blocks on justification/allowance/match/completion.

**Step 4:** `cd src && make test` → PASS. `make build` → clean. Manual: run app, confirm log file appears in App Support.
**Step 5: Commit** `feat: enriched per-cycle debug log with rotation`.

---

## Phase F — UI (build + manual QA; no unit tests)

### Task 17: Setup screen — match-confirm + "how you did it" hint

**Files:** Modify `Sources/AccountaBall/Views/TaskSetupView.swift` (read first).

- After a row's task+context are filled (debounced), call `engine.proposeMatch`. If a match returns, show an inline card: *"Looks like '<title>' — a task you finished before. Bring back what you learned?"* **[Yes] [No, fresh task]**. Yes → confirm-link (Task 15). Show the matched task's latest `steps` as a collapsible "Here's how you did it last time" hint.
- `make build` → clean; **manual QA**: type a previously-completed task, see the prompt; confirm Yes links it.
- **Commit** `feat: setup screen task-match confirm + prior-steps hint`.

### Task 18: Allowance confirm-on-reuse + recap UI

**Files:** Modify `Sources/AccountaBall/Views/OffTaskView.swift`, `AccountaProgressView.swift`, `CompletionView.swift` (read each first).

- **Allowance confirm:** when the engine is about to apply an allowance with `needsConfirmation == true`, surface a one-time prompt on the ball: *"Still counts toward '<task>'? — <rule>"* **[Yes]→clear flag / [No]→delete allowance**. Wire via a `state.pendingAllowanceConfirm` published property.
- **Recap UI:** in the progress panel and completion summary, render each finished task's **total time**, summary, steps list, and (if present) the `comparison` line, from the `TaskRecap`/`TaskCompletion` on `state`.
- `make build` → clean; **manual QA** per the checklist in Task 19.
- **Commit** `feat: allowance confirm-on-reuse + per-task recap UI`.

---

## Phase G — Docs, full verification, manual QA

### Task 19: Update docs + decision records + full manual QA

**Files (outer repo — these are docs, commit in the outer AccountaBall repo):**
- Modify: `CLAUDE.md` — Key Constraints: macOS 13 → **14 (Sonoma)**; add SwiftData; note AI provider = Ollama default / OpenRouter optional via `AI_PROVIDER`; add env vars.
- Create: `planning/decisions/2026-06-03-macos-14-swiftdata.md`
- Create: `planning/decisions/2026-06-03-ollama-default-provider.md`
- Modify: `progress.md` — add v3 status.

**Manual QA checklist** (run `cd src && AI_PROVIDER=ollama make run`, Ollama running with a pulled model; then repeat with `AI_PROVIDER=openrouter OPENROUTER_API_KEY=…`):
- [ ] Per-cycle entries appear in the log; timeline persists across relaunch (SwiftData file in App Support).
- [ ] Off-task → excuse → **justified** creates an allowance; same activity no longer flags and time credits to the task.
- [ ] Off-task → excuse → **not justified** is still logged (check SwiftData / log) but creates no allowance.
- [ ] Completing a task shows recap: total time + summary + steps.
- [ ] Repeating a previously-finished task: match prompt appears; on Yes, allowance confirm-on-reuse fires once; completion shows faster/slower comparison line.
- [ ] Missing-provider hint shows when `AI_PROVIDER=openrouter` without a key, or Ollama not running.
- [ ] All v2 visual flows still work (welcome bounce, setup table, edge ball, progress, 3-point completion).

**Full test run:** `cd src && make test` → Expected: `✓ All N tests passed`.

**Step — Commit (outer repo):** `git add CLAUDE.md planning/decisions progress.md && git commit -m "docs: update constraints + decision records for v3"`

---

## Task dependency order

1 → 2 → 3 → 4 → 5 (pure, parallel-safe) → 6 → 7 → 8 → 9 → 10 → 11 → 12 → 13 → 14 → 15 → 16 → 17 → 18 → 19.

Tasks 1–10 and 16 are TDD with `make test` gates. Tasks 11–15 mix unit tests + `make build`. Tasks 17–18 are build + manual QA. Task 19 is docs + full manual QA.
