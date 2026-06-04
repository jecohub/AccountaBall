# AccountaBall v3.1 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Use superpowers:test-driven-development for every logic task.

**Goal:** Fix the root cause of v3's false nagging (AI errors silently scored as off-task), add a settle window + session-only prompting, and build the rich session-completion breakdown (mechanical timeline + one AI call for per-task commentary).

**Architecture:** Add a provider `healthCheck` + an `.aiUnavailable` phase that pauses the watch loop and auto-resumes. Make classify/excuse errors *skip* a cycle instead of scoring off-task. Add an injectable clock to the engine for a 15s settle window, and gate off-task prompts to `appPhase == .session`. Build session recaps mechanically from the per-cycle timeline we already persist, with one AI call for per-task prose; comparisons are computed locally so numbers are always correct.

**Tech Stack:** Swift 5.9 / SwiftUI / SwiftData (macOS 14+). Tests run on the custom micro-runner (`make test`), NOT XCTest. The runner is async-deadlock-sensitive — keep new suites inside the single top-level `Task { @MainActor in await runAllTests() }` in `Tests/TestRunner/main.swift`; never add semaphores or `DispatchGroup.wait()` on the main thread.

**Working dir:** all code paths below are relative to `/Users/jericodelacruz/Desktop/AccountaBall/src` (its own git repo). Docs (final task) live in the outer repo `/Users/jericodelacruz/Desktop/AccountaBall`.

**Conventions reminder:**
- New test suite = a `func runXxxTests()` (or `func runXxxTests() async` if it awaits) using `suite("name") { ... }` / `await suite("name") { ... }` and `expect(cond, "msg")`. Register it in `Tests/TestRunner/main.swift` → `runAllTests()` (sync suites in the top block; async suites in the `await` block).
- In-memory SwiftData: `try AccountaBallStore.makeContainer(inMemory: true)`; `container.mainContext`.
- Build app only: `make -C <src> build`. Run tests: `make -C <src> test` (no `timeout` on macOS; if you must guard a hang, background the binary with a `sleep … && kill` watchdog).
- Engine methods are `@MainActor`; tests await them directly.

---

## Phase A — Provider availability (fixes #1, #2, #3 at the root)

### Task 1: `healthCheck()` on the AIService protocol + implementations

**Files:**
- Modify: `Sources/AccountaBall/Services/AIService.swift`
- Modify: `Sources/AccountaBall/Services/OllamaAIService.swift`
- Modify: `Sources/AccountaBall/Services/OpenRouterAIService.swift`
- Modify: `Sources/AccountaBall/Services/ClaudeAIService.swift`
- All existing test fakes that conform to `AIService` (search: `: AIService`) — add the method so they keep compiling.

**Step 1: Add to the protocol** (`AIService.swift`), after `matchTask`:
```swift
    /// Cheap reachability probe. Returns false on connection failure or, for
    /// local providers, when the configured model isn't available. Non-throwing
    /// by design — callers branch on the Bool, never treat a failure as off-task.
    func healthCheck() async -> Bool
```

**Step 2: Ollama implementation** (`OllamaAIService.swift`): hits `/api/tags` and verifies the configured model is present.
```swift
    func healthCheck() async -> Bool {
        guard let url = URL(string: "\(host)/api/tags") else { return false }
        do {
            let (data, resp) = try await session.data(from: url)
            guard (resp as? HTTPURLResponse)?.statusCode == 200 else { return false }
            let obj = try JSONSerialization.jsonObject(with: data) as? [String: Any]
            let names = (obj?["models"] as? [[String: Any]] ?? []).compactMap { $0["name"] as? String }
            let base = model.split(separator: ":").first.map(String.init) ?? model
            return names.contains(model) || names.contains { $0.hasPrefix(base + ":") || $0 == base }
        } catch {
            return false
        }
    }
```

**Step 3: OpenRouter implementation** (`OpenRouterAIService.swift`) — key presence is enough (a bad key surfaces on first real call):
```swift
    func healthCheck() async -> Bool { !apiKey.isEmpty }
```
(Read the file first to confirm the stored property name is `apiKey`.)

**Step 4: Claude stub** (`ClaudeAIService.swift`):
```swift
    func healthCheck() async -> Bool { true }
```

**Step 5:** Add `func healthCheck() async -> Bool { true }` to every test fake that conforms to `AIService` (e.g. in `EngineAllowanceTests.swift`, `EngineCompletionTests.swift`, `EngineMatchTests.swift`, `AccountabilityEngineTests.swift`, and any in `ClaudeAIServiceTests`/`OllamaServiceTests`). Search first: `grep -rln ": AIService" Tests`.

**Step 6:** `make -C <src> build` → clean (this is a compile-gate task; the real network call isn't unit-tested).

**Step 7: Commit** `feat: add AIService.healthCheck (provider reachability probe)`.

---

### Task 2: `.aiUnavailable` phase + state hint

**Files:**
- Modify: `Sources/AccountaBall/Models/AppPhase.swift`
- Modify: `Sources/AccountaBall/Models/AppState.swift`

**Step 1:** Add the case to `AppPhase`:
```swift
    case aiUnavailable
```

**Step 2:** Add to `AppState` (near `setupHint`):
```swift
    /// Set when the AI provider is unreachable; shown on the pause card.
    @Published var aiUnavailableHint: String? = nil
```

**Step 3:** `make -C <src> build` → clean (exhaustive `switch`es over `AppPhase` may now error; if `RootCoordinatorView` switches over phases, add a placeholder `case .aiUnavailable: EmptyView()` for now — Task 5 fills it in).

**Step 4: Commit** `feat: add .aiUnavailable phase + aiUnavailableHint`.

---

### Task 3: classify/excuse errors must never score off-task

**Files:**
- Modify: `Sources/AccountaBall/Engine/AccountabilityEngine.swift` (the capture loop in `start()`, ~line 34-51)
- Test: `Tests/AccountaBallTests/EngineErrorHandlingTests.swift` (new)

**Context:** today `start()` does `(try? await classifyMulti(...)) ?? .offTask(label: "")`. We must distinguish "AI said off-task" from "AI call failed". Refactor the loop to call classify in a `do/catch`; on throw, **skip the cycle** (record nothing, don't touch `suspicionCount`) and signal unavailability (Task 4 consumes the signal).

**Step 1: Write the failing test.** Because the loop closure is hard to drive directly, extract the decision into a testable method. Add to the engine:
```swift
    /// Process one classification *outcome*. `result == nil` means the AI call
    /// failed — skip the cycle entirely (no suspicion bump, never off-task).
    @MainActor
    func processCycle(_ result: MultiTaskResult?) {
        guard let result else { return }   // AI error: skip, do not penalize
        processResult(result)
    }
```
Test (`EngineErrorHandlingTests.swift`, async suite):
```swift
@MainActor
func runEngineErrorHandlingTests() async {
    await suite("EngineErrorHandling_errorIsNotOffTask") {
        guard let container = try? AccountaBallStore.makeContainer(inMemory: true) else { expect(false, "container"); return }
        let s = AppState(); s.tasks = [TaskItem(task: "write", context: "")]; s.startSession()
        let engine = AccountabilityEngine(state: s, captureService: ScreenCaptureService(), ocrService: OCRService(), aiService: AlwaysOnTaskAI(), notificationService: NotificationService())
        engine.modelContext = container.mainContext
        engine.beginSession(tasks: s.tasks)
        // Two failed cycles must NOT escalate to the off-task prompt.
        engine.processCycle(nil)
        engine.processCycle(nil)
        expect(s.appPhase == .session, "AI errors keep us in session, not offTask")
        expect(s.ballState != .offTask, "AI errors never set offTask ball state")
    }
}
```
(Define a tiny `AlwaysOnTaskAI: AIService` fake in this file, or reuse one — include `healthCheck() async -> Bool { true }`.)

**Step 2:** Register `await runEngineErrorHandlingTests()` in `main.swift`. Run `make test` → the new suite should already pass IF `processCycle` is added; if you wrote the test before the method, it fails to compile (that's the red). Add `processCycle`, re-run → PASS.

**Step 3:** Rewire `start()` to use the new path. Replace:
```swift
let result = (try? await self.aiService.classifyMulti(...)) ?? .offTask(label: "")
... self.processResult(result)
```
with:
```swift
let result: MultiTaskResult?
do {
    result = try await self.aiService.classifyMulti(tasks: activeTasks, screenText: text, allowanceRulesByIndex: self.allowanceRulesByIndex(for: activeTasks))
} catch {
    dbg("classifyMulti failed (skipping cycle): \(error)")
    await self.enterAIUnavailable(reason: error)   // added in Task 4
    result = nil
}
... self.processCycle(result)
```
(Until Task 4 exists, temporarily call only `result = nil` in the catch; wire `enterAIUnavailable` in Task 4. Note the loop currently passes `allowanceRulesByIndex: [:]` — switch it to the real `allowanceRulesByIndex(for:)` so revived allowances actually apply.)

**Step 4:** `make test` → PASS, `make build` → clean.

**Step 5: Commit** `fix: AI classify errors skip the cycle instead of scoring off-task`.

---

### Task 4: enter `.aiUnavailable` on failure + auto-resume via health polling

**Files:**
- Modify: `Sources/AccountaBall/Engine/AccountabilityEngine.swift`
- Test: `Tests/AccountaBallTests/EngineAvailabilityTests.swift` (new)

**Step 1:** Add to the engine:
```swift
    private var healthPollTask: Task<Void, Never>?

    @MainActor
    func enterAIUnavailable(reason: Error? = nil) {
        guard state.appPhase != .aiUnavailable else { return }
        dbg("AI unavailable: \(reason.map { "\($0)" } ?? "health check failed")")
        stop()                                   // pause the capture loop
        state.aiUnavailableHint = aiHint()
        state.ballState = .idle
        state.appPhase = .aiUnavailable
        startHealthPolling()
    }

    /// Provider-specific setup hint. Keep generic; the engine doesn't know the
    /// concrete provider type, so phrase for the default (Ollama) + mention env.
    @MainActor
    private func aiHint() -> String {
        "Can't reach the AI. If using Ollama, run it and pull the model (e.g. `ollama run qwen2.5:7b`); or set AI_PROVIDER/OPENROUTER_API_KEY."
    }

    @MainActor
    private func startHealthPolling() {
        healthPollTask?.cancel()
        healthPollTask = Task { @MainActor in
            while !Task.isCancelled {
                if await aiService.healthCheck() {
                    recoverFromAIUnavailable()
                    return
                }
                try? await Task.sleep(for: .seconds(5))
            }
        }
    }

    @MainActor
    func recoverFromAIUnavailable() {
        guard state.appPhase == .aiUnavailable else { return }
        healthPollTask?.cancel(); healthPollTask = nil
        state.aiUnavailableHint = nil
        dbg("AI recovered -> resuming session")
        state.appPhase = .session
        state.ballState = .onTask
        state.isCapturing = true                 // AppDelegate's watcher restarts start()
        resetSettleWindow()                      // added in Task 6 (no-op stub until then)
    }
```
Add a temporary `func resetSettleWindow() {}` stub now; Task 6 implements it.

**Step 2: Failing test** (`EngineAvailabilityTests.swift`): a fake whose `healthCheck` flips false→true after N calls proves pause→auto-resume.
```swift
@MainActor
func runEngineAvailabilityTests() async {
    await suite("EngineAvailability_pauseAndResume") {
        let s = AppState(); s.tasks = [TaskItem(task: "x", context: "")]; s.startSession()
        let ai = FlakyAI()                       // healthCheck: false first call, true after
        let engine = AccountabilityEngine(state: s, captureService: ScreenCaptureService(), ocrService: OCRService(), aiService: ai, notificationService: NotificationService())
        guard let c = try? AccountaBallStore.makeContainer(inMemory: true) else { expect(false,"c"); return }
        engine.modelContext = c.mainContext; engine.beginSession(tasks: s.tasks)

        engine.enterAIUnavailable()
        expect(s.appPhase == .aiUnavailable, "entered aiUnavailable")
        expect(s.aiUnavailableHint != nil, "hint set")

        // Poll once manually (don't wait on the 5s loop in a test):
        let ok = await ai.healthCheck()
        expect(ok, "fake reports healthy on 2nd call")
        engine.recoverFromAIUnavailable()
        expect(s.appPhase == .session, "resumed to session")
        expect(s.aiUnavailableHint == nil, "hint cleared")
    }
}
```
Define `FlakyAI: AIService` in-file: a counter that returns `healthCheck()` false on the first call, true after; other methods minimal.

**Step 3:** Register `await runEngineAvailabilityTests()`. Implement until green. In Task 3's `start()` catch block, replace the temporary `result = nil` with the real `await self.enterAIUnavailable(reason: error)` call (already shown).

**Step 4:** Also health-check at launch: in `AppDelegate.applicationDidFinishLaunching`, after building the engine, add a startup probe:
```swift
Task { @MainActor in
    if await aiService.healthCheck() == false { eng.enterAIUnavailable() }
}
```
(Build-only; manual QA verifies.)

**Step 5:** `make test` → PASS, `make build` → clean.

**Step 6: Commit** `feat: pause accountability on AI failure with auto-resume health polling`.

---

### Task 5: AI-unavailable card + RootCoordinator wiring

**Files:**
- Create: `Sources/AccountaBall/Views/AIUnavailableView.swift`
- Modify: `Sources/AccountaBall/Views/RootCoordinatorView.swift`

**Step 1:** New view:
```swift
import SwiftUI

struct AIUnavailableView: View {
    @EnvironmentObject var state: AppState
    var body: some View {
        VStack(spacing: 14) {
            BasketballView(size: 64, showFace: .sad)   // confirm the showFace API/case name
            Text("AI unavailable")
                .font(.system(size: 15, weight: .semibold)).foregroundStyle(.white)
            Text(state.aiUnavailableHint ?? "Can't reach the AI provider.")
                .font(.system(size: 12)).foregroundStyle(.white.opacity(0.75))
                .multilineTextAlignment(.center).fixedSize(horizontal: false, vertical: true)
            ProgressView().controlSize(.small).tint(.white)
            Text("Retrying automatically…").font(.system(size: 11)).foregroundStyle(.white.opacity(0.5))
        }
        .padding(20).frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color.black.opacity(0.9))
    }
}
```
(Read `BallView.swift`/`BasketballView` first to confirm the face-state API; fall back to a plain ball if `.sad` doesn't exist.)

**Step 2:** Add the case in `RootCoordinatorView.body`:
```swift
            case .aiUnavailable:
                AIUnavailableView().transition(.opacity)
```
Remove any placeholder `EmptyView()` from Task 2.

**Step 3:** Ensure the panel resizes for this phase — check `FloatingPanel.resize(for:)` and `AppDelegate`'s phase-watcher handle `.aiUnavailable` (give it a reasonable size, e.g. the off-task card size).

**Step 4:** `make build` → clean. **Manual QA:** with Ollama stopped, launch → card appears; start Ollama + pull model → within ~5s it auto-resumes to the session.

**Step 5: Commit** `feat: AI-unavailable pause card + auto-resume UI`.

---

## Phase B — Grace window (#1) + prompt suppression (#4)

### Task 6: injectable clock + 15s settle window

**Files:**
- Modify: `Sources/AccountaBall/Engine/AccountabilityEngine.swift`
- Test: `Tests/AccountaBallTests/EngineSettleWindowTests.swift` (new)

**Step 1:** Add to the engine:
```swift
    /// Injectable clock for deterministic settle-window tests.
    var now: () -> Date = { Date() }
    let settleWindow: TimeInterval = 15
    private var settleUntil: Date = .distantPast

    @MainActor
    func resetSettleWindow() { settleUntil = now().addingTimeInterval(settleWindow) }

    private var inSettleWindow: Bool { now() < settleUntil }
```
Call `resetSettleWindow()` from `beginSession(...)` and `resumeAfterExcuse()` (and it's already called in `recoverFromAIUnavailable`). Replace the Task-4 stub with this real implementation.

**Step 2: Failing test:**
```swift
@MainActor
func runEngineSettleWindowTests() async {
    await suite("EngineSettleWindow_suppressesEarlyPrompt") {
        guard let c = try? AccountaBallStore.makeContainer(inMemory: true) else { expect(false,"c"); return }
        let s = AppState(); s.tasks = [TaskItem(task: "x", context: "")]; s.startSession()
        let engine = AccountabilityEngine(state: s, captureService: ScreenCaptureService(), ocrService: OCRService(), aiService: AlwaysOnTaskAI(), notificationService: NotificationService())
        engine.modelContext = c.mainContext
        var fakeNow = Date()
        engine.now = { fakeNow }
        engine.beginSession(tasks: s.tasks)            // arms settle window: now+15
        // Two off-task reads inside the window must NOT prompt:
        engine.processResult(.offTask(label: "yt"))
        engine.processResult(.offTask(label: "yt"))
        expect(s.appPhase == .session, "no prompt inside settle window")
        // Advance past the window; two more off-task reads DO prompt:
        fakeNow = fakeNow.addingTimeInterval(16)
        engine.processResult(.offTask(label: "yt"))
        engine.processResult(.offTask(label: "yt"))
        expect(s.appPhase == .offTask, "prompts after settle window")
    }
}
```

**Step 3:** In `processResult`, in the `.offTask` branch, guard the escalation:
```swift
            if suspicionCount >= 2 && !inSettleWindow {
                ...escalate to offTask...
            }
```
(Keep recording/`suspicionCount++` as-is; only the escalation is gated. Confirm `beginSession` arms the window so `s.startSession()` alone — used by some tests — doesn't.)

**Step 4:** Register, `make test` → PASS, `make build` → clean. Re-run the suite a couple times (determinism).

**Step 5: Commit** `feat: 15s settle window suppresses early off-task prompts`.

---

### Task 7: prompt only when `appPhase == .session`

**Files:**
- Modify: `Sources/AccountaBall/Engine/AccountabilityEngine.swift` (`processResult`)
- Test: `Tests/AccountaBallTests/EnginePromptGateTests.swift` (new)

**Step 1: Failing test** — an off-task read while `.progress` must record but not interrupt:
```swift
@MainActor
func runEnginePromptGateTests() async {
    await suite("EnginePromptGate_noPromptOutsideSession") {
        guard let c = try? AccountaBallStore.makeContainer(inMemory: true) else { expect(false,"c"); return }
        let s = AppState(); s.tasks = [TaskItem(task: "x", context: "")]; s.startSession()
        let engine = AccountabilityEngine(state: s, captureService: ScreenCaptureService(), ocrService: OCRService(), aiService: AlwaysOnTaskAI(), notificationService: NotificationService())
        engine.modelContext = c.mainContext; engine.beginSession(tasks: s.tasks)
        engine.now = { Date().addingTimeInterval(3600) }   // past settle window
        s.appPhase = .progress                              // user is in the session log
        engine.processResult(.offTask(label: "yt"))
        engine.processResult(.offTask(label: "yt"))
        expect(s.appPhase == .progress, "stays on the log screen; no prompt")
        let entries = (try? c.mainContext.fetch(FetchDescriptor<TimelineEntry>())) ?? []
        expect(entries.count == 2, "timeline still recorded while suppressed")
    }
}
```

**Step 2:** In `processResult`, change the existing top guard. Today it's `guard state.appPhase != .offTask else { ... }`. Replace the escalation condition so it only prompts in `.session`:
- Keep recording for any phase (move the `record(...)` calls before/outside the gate).
- Only run the suspicion→offTask escalation when `state.appPhase == .session`.

Concretely, in the `.offTask` case:
```swift
            if suspicionCount >= 2 && !inSettleWindow && state.appPhase == .session {
                ...escalate...
            }
```
and keep the early-return guard for `.offTask` phase (don't double-prompt). For `.onTask`/`.done`, recording + state updates are fine in any phase, but avoid forcing `.session` UI changes when the user is elsewhere — review the `.onTask` branch so it doesn't yank `appPhase`. (It currently doesn't set `appPhase`; just `ballState`/`activeTaskIndex`. Leave those.)

**Step 3:** Register, `make test` → PASS, `make build` → clean.

**Step 4: Commit** `fix: only raise off-task prompts during an active session`.

---

## Phase C — Session-completion breakdown (#5)

### Task 8: `TimelineRange` + range builder

**Files:**
- Modify: `Sources/AccountaBall/Util/TimelineCoalescer.swift`
- Create: `Sources/AccountaBall/Models/SessionRecap.swift`
- Test: `Tests/AccountaBallTests/TimelineRangeTests.swift` (new)

**Step 1:** New types (`SessionRecap.swift`):
```swift
import Foundation

struct TimelineRange: Equatable {
    let startOffset: TimeInterval   // seconds from session start
    let endOffset: TimeInterval
    let label: String
    let taskIndex: Int?             // nil = off-task
}

struct PerTaskComment: Equatable {
    let taskTitle: String
    let comment: String
    let suggestion: String?
}

struct SessionRecap: Equatable {
    let ranges: [TimelineRange]
    let perTask: [PerTaskComment]
}
```

**Step 2: Failing test** (`TimelineRangeTests.swift`, sync suite): coalesce contiguous `(taskIndex,label)`; final range extends one cycle (5s).
```swift
func runTimelineRangeTests() {
    suite("TimelineRange_coalesce") {
        let start = Date()
        func e(_ s: TimeInterval, _ idx: Int?, _ l: String) -> (at: Date, taskIndex: Int?, label: String) {
            (at: start.addingTimeInterval(s), taskIndex: idx, label: l)
        }
        let entries = [e(0,0,"vscode"), e(5,0,"vscode"), e(10,nil,"linkedin"), e(15,nil,"linkedin")]
        let ranges = TimelineCoalescer.ranges(sessionStart: start, entries: entries, cycleSeconds: 5)
        expect(ranges.count == 2, "two coalesced ranges")
        expect(ranges[0] == TimelineRange(startOffset: 0, endOffset: 10, label: "vscode", taskIndex: 0), "first range 0–10 vscode")
        expect(ranges[1] == TimelineRange(startOffset: 10, endOffset: 20, label: "linkedin", taskIndex: nil), "second range 10–20 off-task")
    }
}
```

**Step 3:** Implement `ranges` in `TimelineCoalescer`:
```swift
    static func ranges(sessionStart: Date,
                       entries: [(at: Date, taskIndex: Int?, label: String)],
                       cycleSeconds: TimeInterval = 5) -> [TimelineRange] {
        let sorted = entries.sorted { $0.at < $1.at }
        var out: [TimelineRange] = []
        var i = 0
        while i < sorted.count {
            let startOff = sorted[i].at.timeIntervalSince(sessionStart)
            let idx = sorted[i].taskIndex, label = sorted[i].label
            var j = i
            while j + 1 < sorted.count && sorted[j+1].taskIndex == idx && sorted[j+1].label == label { j += 1 }
            // End = next group's start, or last entry + one cycle.
            let endOff: TimeInterval = (j + 1 < sorted.count)
                ? sorted[j+1].at.timeIntervalSince(sessionStart)
                : sorted[j].at.timeIntervalSince(sessionStart) + cycleSeconds
            out.append(TimelineRange(startOffset: startOff, endOffset: endOff, label: label, taskIndex: idx))
            i = j + 1
        }
        return out
    }
```

**Step 4:** Register `runTimelineRangeTests()` (sync block). `make test` → PASS.

**Step 5: Commit** `feat: TimelineRange + coalescer range builder`.

---

### Task 9: `summarizeSession` on the AIService protocol + impls

**Files:**
- Modify: `Sources/AccountaBall/Services/AIService.swift`
- Modify: `Sources/AccountaBall/Util/AIPrompts.swift` (prompt builder + parser — read it first to match the existing style)
- Modify: `OpenRouterAIService.swift`, `OllamaAIService.swift`, `ClaudeAIService.swift`
- Modify: all `AIService` fakes (add the method)
- Test: `Tests/AccountaBallTests/SummarizeSessionParseTests.swift` (new — test the pure parser)

**Step 1:** Input type (add to `SessionRecap.swift`):
```swift
struct PerTaskSessionInput: Equatable {
    let title: String
    let durationSeconds: TimeInterval
    let lastDurationSeconds: TimeInterval?
    let averageSeconds: TimeInterval?
    let offTaskCount: Int
    let steps: [String]
    let localComparison: String   // authoritative, computed locally
}
```

**Step 2:** Protocol method:
```swift
    func summarizeSession(perTask: [PerTaskSessionInput]) async throws -> [PerTaskComment]
```

**Step 3:** Pure parser in `AIPrompts.swift` (the model returns `{"tasks":[{"title","comment","suggestion"}]}`):
```swift
    static func parseSessionComments(_ json: String, titles: [String]) -> [PerTaskComment] {
        guard let data = json.data(using: .utf8),
              let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let arr = obj["tasks"] as? [[String: Any]] else { return [] }
        return arr.compactMap { d in
            guard let t = d["title"] as? String else { return nil }
            let c = (d["comment"] as? String) ?? ""
            let sug = (d["suggestion"] as? String).flatMap { $0.isEmpty ? nil : $0 }
            return PerTaskComment(taskTitle: t, comment: c, suggestion: sug)
        }
    }
```
Also add a `buildSessionPrompt(perTask:)` that lists each task's title, this/last/avg durations, off-task count, steps, and the local comparison, instructing the model to NOT invent numbers (the local comparison is authoritative) and to return the JSON shape above; `suggestion` only "if there's any".

**Step 4: Failing test** (parser only — deterministic, no network):
```swift
func runSummarizeSessionParseTests() {
    suite("SummarizeSessionParse") {
        let json = #"{"tasks":[{"title":"Write proposal","comment":"Faster than last time.","suggestion":"Snooze Slack."},{"title":"Review","comment":"Mostly browsing.","suggestion":""}]}"#
        let out = AIPrompts.parseSessionComments(json, titles: ["Write proposal","Review"])
        expect(out.count == 2, "two comments parsed")
        expect(out[0].suggestion == "Snooze Slack.", "suggestion kept")
        expect(out[1].suggestion == nil, "empty suggestion -> nil")
    }
}
```

**Step 5:** Implement `summarizeSession` in each provider: build prompt → call the chat endpoint (mirror `summarizeTask`'s plumbing) → `AIPrompts.parseSessionComments(reply, titles:)`. Claude stub: `return []`. Add `summarizeSession` to all fakes (return `[]` or a fixed value).

**Step 6:** Register `runSummarizeSessionParseTests()`. `make test` → PASS, `make build` → clean.

**Step 7: Commit** `feat: AIService.summarizeSession + JSON parser`.

---

### Task 10: engine builds the SessionRecap on completion

**Files:**
- Modify: `Sources/AccountaBall/Engine/AccountabilityEngine.swift`
- Modify: `Sources/AccountaBall/Models/AppState.swift` (add `@Published var sessionRecap: SessionRecap? = nil`)
- Test: `Tests/AccountaBallTests/EngineSessionRecapTests.swift` (new)

**Step 1:** Add `@Published var sessionRecap: SessionRecap? = nil` to `AppState`.

**Step 2:** Engine method:
```swift
    /// Build the end-of-session breakdown: mechanical timeline ranges + one AI
    /// call for per-task commentary. Comparisons are computed locally. Safe to
    /// call once all tasks are complete. Never throws — AI failure degrades to
    /// timeline + local comparison strings.
    @MainActor
    func finalizeSessionRecap() async {
        guard let session = currentSession, let ctx = modelContext else { return }

        let ranges = TimelineCoalescer.ranges(
            sessionStart: session.startedAt,
            entries: session.entries.map { (at: $0.at, taskIndex: $0.taskIndex, label: $0.label) }
        )

        var inputs: [PerTaskSessionInput] = []
        for task in state.tasks {
            let dur = task.timeOnTask
            let normalized = TaskMatcher.normalize(task.task)
            let kt = try? ctx.fetch(FetchDescriptor<KnowledgeTask>(predicate: #Predicate { $0.normalizedTitle == normalized })).first
            // Prior completions = those finished before THIS session started.
            let prior = (kt?.completions ?? []).filter { $0.completedAt < session.startedAt }
            let last = prior.sorted { $0.completedAt > $1.completedAt }.first?.duration
            let avg: TimeInterval? = prior.isEmpty ? nil : prior.map { $0.duration }.reduce(0, +) / Double(prior.count)
            let comparison = Self.comparisonString(current: dur, last: last, average: avg)
            let offCount = session.justifications.filter { !$0.justified }.count
            let steps = TimelineCoalescer.labelsForTask(index: state.tasks.firstIndex(where: { $0.task == task.task }) ?? -1,
                                                        reads: session.entries.sorted { $0.at < $1.at }.map { ($0.taskIndex, $0.label) })
            inputs.append(PerTaskSessionInput(title: task.task, durationSeconds: dur,
                lastDurationSeconds: last, averageSeconds: avg, offTaskCount: offCount,
                steps: steps, localComparison: comparison))
        }

        var comments: [PerTaskComment] = []
        do {
            comments = try await aiService.summarizeSession(perTask: inputs)
        } catch {
            dbg("summarizeSession failed: \(error)")
        }
        if comments.isEmpty {   // fallback: local comparison only
            comments = inputs.map { PerTaskComment(taskTitle: $0.title, comment: $0.localComparison, suggestion: nil) }
        }
        state.sessionRecap = SessionRecap(ranges: ranges, perTask: comments)
    }

    static func comparisonString(current: TimeInterval, last: TimeInterval?, average: TimeInterval?) -> String {
        func mins(_ t: TimeInterval) -> String { "\(Int((t/60).rounded()))m" }
        guard let last else { return "First time finishing this — \(mins(current))." }
        let d = DurationDelta.compare(current: current, previous: last)
        var s = d.fasterThanPrevious ? "Faster than last time (\(mins(last)) → \(mins(current)))." : "Slower than last time (\(mins(last)) → \(mins(current)))."
        if let average { s += " Avg \(mins(average))." }
        return s
    }
```

**Step 3: Failing test** (`EngineSessionRecapTests.swift`): seed a prior completion, record a couple of timeline entries, call `finalizeSessionRecap`, assert ranges + a comment exist and the local-comparison fallback works (use a fake whose `summarizeSession` returns `[]`).
```swift
@MainActor
func runEngineSessionRecapTests() async {
    await suite("EngineSessionRecap") {
        guard let c = try? AccountaBallStore.makeContainer(inMemory: true) else { expect(false,"c"); return }
        let ctx = c.mainContext
        // Prior completion (older session): 900s
        let kt = KnowledgeTask(normalizedTitle: "write proposal", lastCompletedAt: .now.addingTimeInterval(-86400))
        kt.completions.append(TaskCompletion(completedAt: .now.addingTimeInterval(-86400), duration: 900, summary: "s", steps: ["a"], offTaskCount: 0))
        ctx.insert(kt); try? ctx.save()

        let s = AppState(); s.tasks = [TaskItem(task: "Write proposal", context: "")]; s.startSession()
        s.tasks[0].timeOnTask = 600
        let engine = AccountabilityEngine(state: s, captureService: ScreenCaptureService(), ocrService: OCRService(), aiService: EmptySessionAI(), notificationService: NotificationService())
        engine.modelContext = ctx; engine.beginSession(tasks: s.tasks)
        engine.record(taskIndex: 0, label: "vscode")

        await engine.finalizeSessionRecap()
        expect(s.sessionRecap != nil, "recap built")
        expect(s.sessionRecap?.ranges.isEmpty == false, "timeline ranges present")
        let c0 = s.sessionRecap?.perTask.first
        expect(c0?.comment.contains("Faster") == true, "local comparison says faster (600 < 900)")
    }
}
```
(`EmptySessionAI` returns `[]` from `summarizeSession`.)

**Step 4:** Wire the call. In `AppDelegate`'s phase-watcher `Task`, when `phase == .complete` and not already finalized, call `await self.engine?.finalizeSessionRecap()`. (Guard against double-calls with a flag.) This covers both the AI `.done` path and the manual progress-panel checkoff.

**Step 5:** Register, `make test` → PASS, `make build` → clean.

**Step 6: Commit** `feat: build end-of-session recap (timeline + per-task commentary)`.

---

### Task 11: completion screen renders the breakdown

**Files:**
- Modify: `Sources/AccountaBall/Views/CompletionView.swift`

**Step 1:** Below the existing stats block (and before/with the recap cards from v3 Task 18), add a timeline section + per-task comments from `state.sessionRecap`:
```swift
if let recap = state.sessionRecap {
    ScrollView {
        VStack(alignment: .leading, spacing: 12) {
            if !recap.ranges.isEmpty {
                Text("Timeline").font(.system(size: 13, weight: .semibold)).foregroundStyle(.white.opacity(0.8))
                ForEach(Array(recap.ranges.enumerated()), id: \.offset) { _, r in
                    HStack(alignment: .top, spacing: 8) {
                        Text("\(mmss(r.startOffset))–\(mmss(r.endOffset))")
                            .font(.system(size: 11, design: .monospaced)).foregroundStyle(.white.opacity(0.6))
                            .frame(width: 96, alignment: .leading)
                        Text(r.label + (r.taskIndex == nil ? "  (off-task)" : ""))
                            .font(.system(size: 12)).foregroundStyle(r.taskIndex == nil ? .orange.opacity(0.85) : .white.opacity(0.85))
                    }
                }
            }
            if !recap.perTask.isEmpty {
                Text("Per task").font(.system(size: 13, weight: .semibold)).foregroundStyle(.white.opacity(0.8)).padding(.top, 6)
                ForEach(Array(recap.perTask.enumerated()), id: \.offset) { _, c in
                    VStack(alignment: .leading, spacing: 3) {
                        Text(c.taskTitle).font(.system(size: 12, weight: .semibold)).foregroundStyle(.white)
                        Text(c.comment).font(.system(size: 12)).foregroundStyle(.white.opacity(0.8)).fixedSize(horizontal: false, vertical: true)
                        if let s = c.suggestion { Text("💡 " + s).font(.system(size: 11)).foregroundStyle(.orange.opacity(0.9)) }
                    }
                    .padding(10).frame(maxWidth: .infinity, alignment: .leading)
                    .background(Color.white.opacity(0.06)).clipShape(RoundedRectangle(cornerRadius: 10))
                }
            }
        }
    }
    .frame(maxHeight: 260)
}
```
Add the `mmss` helper:
```swift
    private func mmss(_ t: TimeInterval) -> String { let s = Int(t); return String(format: "%02d:%02d", s/60, s%60) }
```
(The v3 `state.recaps` per-task cards can stay or be superseded by this — keep both for now; dedupe is a polish item, not in scope.)

**Step 2:** `make build` → clean. **Manual QA:** finish a session → timeline ranges + per-task comments render.

**Step 3: Commit** `feat: render session timeline + per-task commentary on completion`.

---

## Phase D — Verification, docs, manual QA

### Task 12: full verification + docs + progress

**Files (outer repo `/Users/jericodelacruz/Desktop/AccountaBall`):**
- Modify: `progress.md`

**Step 1:** `make -C <src> test` → expect `✓ All N tests passed`; run it twice (determinism — the runner had an ordering history). `make -C <src> build` → clean.

**Step 2: Manual QA** (Ollama running + `qwen2.5:7b` pulled; then repeat with `AI_PROVIDER=openrouter OPENROUTER_API_KEY=…`):
- [ ] Stop Ollama → launch → "AI unavailable" card; start Ollama → auto-resumes within ~5s. No false off-task prompts while down.
- [ ] Fresh session: no prompt in the first 15s even if you're off-task.
- [ ] Open the session log / progress screen → no "what are you doing?" interruption; timeline still records.
- [ ] Off-task → excuse that's genuinely aligned → accepted (not "get back to your tasks") with Ollama actually running.
- [ ] Finish a session → completion shows the timeline ranges + per-task comments; a repeated task shows faster/slower vs last + avg.

**Step 3:** Update `progress.md`: v3.1 status, what shipped, residual #2/#3 prompt-tuning verdict after re-test, and the still-open OpenRouter key rotation.

**Step 4: Commit (outer repo)** `docs: v3.1 progress — provider availability + settle window + session breakdown`.

---

## Task dependency order

A1 → A2 → A3 → A4 → A5 → B6 → B7 → C8 → C9 → C10 → C11 → D12.

Phase A is the highest-value fix and is independently shippable — **after A5, re-test with Ollama running** to decide whether #2/#3 need any classification-prompt tuning (would be a small extra task in Phase A). Phases B and C don't depend on that verdict.
