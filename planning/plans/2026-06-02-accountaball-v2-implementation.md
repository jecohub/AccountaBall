# AccountaBall v2 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Rebuild AccountaBall with multi-task support, animated app flows (bounce intro, side basketball widget, angry creep, 3-point shot completion), per-task time tracking, and AI-judged excuse evaluation — all in native SwiftUI.

**Architecture:** A new `AppPhase` enum drives a root coordinator view that routes between 7 screens. The existing capture/OCR/AI loop is preserved and extended with multi-task classification. All new UI is pure SwiftUI; animations use `PhaseAnimator` and `keyframeAnimator`.

**Tech Stack:** Swift 5.9+, macOS 14+, SwiftUI, Combine, ScreenCaptureKit, Vision.framework, OpenRouter API. Build: `make test` / `make run` from `src/`.

**Test system:** Custom micro-test runner — `make test`. Add new `runXxxTests()` functions to `Tests/AccountaBallTests/` and register them in `Tests/TestRunner/main.swift`. No XCTest, no Swift Testing.

---

## Phase 1: Foundation Models

### Task 1: AppPhase Enum

**Files:**
- Create: `Sources/AccountaBall/Models/AppPhase.swift`
- Create: `Tests/AccountaBallTests/AppPhaseTests.swift`
- Modify: `Tests/TestRunner/main.swift` (add `runAppPhaseTests()`)

**Step 1: Write the failing test**

Create `Tests/AccountaBallTests/AppPhaseTests.swift`:

```swift
@testable import AccountaBall

func runAppPhaseTests() {
    suite("AppPhaseTests") {
        expect(AppPhase.idle == AppPhase.idle, "idle == idle")
        expect(AppPhase.welcome != AppPhase.setup, "welcome != setup")

        let all: [AppPhase] = [.idle, .welcome, .setup, .session, .offTask, .progress, .complete]
        expect(all.count == 7, "all 7 phases exist")
    }
}
```

Add `runAppPhaseTests()` to `Tests/TestRunner/main.swift` before `reportAndExit()`.

**Step 2: Run — expect FAIL**
```bash
make test
# Expected: error: cannot find type 'AppPhase'
```

**Step 3: Implement**

Create `Sources/AccountaBall/Models/AppPhase.swift`:

```swift
enum AppPhase: Equatable {
    case idle
    case welcome
    case setup
    case session
    case offTask
    case progress
    case complete
}
```

**Step 4: Run — expect PASS**
```bash
make test
```

**Step 5: Commit**
```bash
cd /Users/jericodelacruz/Desktop/AccountaBall/src
git add Sources/AccountaBall/Models/AppPhase.swift Tests/AccountaBallTests/AppPhaseTests.swift Tests/TestRunner/main.swift
git commit -m "feat: add AppPhase enum"
```

---

### Task 2: TaskItem Model

**Files:**
- Create: `Sources/AccountaBall/Models/TaskItem.swift`
- Create: `Tests/AccountaBallTests/TaskItemTests.swift`
- Modify: `Tests/TestRunner/main.swift` (add `runTaskItemTests()`)

**Step 1: Write the failing test**

```swift
import Foundation
@testable import AccountaBall

func runTaskItemTests() {
    suite("TaskItemTests") {
        let item = TaskItem(task: "write proposal", context: "for client meeting")
        expect(item.task == "write proposal", "task stored correctly")
        expect(item.context == "for client meeting", "context stored correctly")
        expect(item.isComplete == false, "starts incomplete")
        expect(item.timeOnTask == 0, "starts with zero time")

        // Codable roundtrip
        if let data = try? JSONEncoder().encode(item),
           let decoded = try? JSONDecoder().decode(TaskItem.self, from: data) {
            expect(decoded.task == item.task, "codable roundtrip preserves task")
            expect(decoded.id == item.id, "codable roundtrip preserves id")
            expect(decoded.isComplete == false, "codable roundtrip preserves isComplete")
        } else {
            expect(false, "codable roundtrip succeeded")
        }

        // Empty item
        let empty = TaskItem()
        expect(empty.task == "", "empty item has blank task")
        expect(empty.context == "", "empty item has blank context")

        // isFilledIn
        expect(!empty.isFilledIn, "empty item is not filled in")
        expect(item.isFilledIn, "item with task+context is filled in")
    }
}
```

**Step 2: Run — expect FAIL**

**Step 3: Implement**

Create `Sources/AccountaBall/Models/TaskItem.swift`:

```swift
import Foundation

struct TaskItem: Codable, Identifiable, Equatable {
    let id: UUID
    var task: String
    var context: String
    var isComplete: Bool
    var timeOnTask: TimeInterval

    init(task: String = "", context: String = "", isComplete: Bool = false, timeOnTask: TimeInterval = 0) {
        self.id = UUID()
        self.task = task
        self.context = context
        self.isComplete = isComplete
        self.timeOnTask = timeOnTask
    }

    var isFilledIn: Bool {
        !task.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty &&
        !context.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }
}
```

**Step 4: Run — expect PASS**

**Step 5: Commit**
```bash
git add Sources/AccountaBall/Models/TaskItem.swift Tests/AccountaBallTests/TaskItemTests.swift Tests/TestRunner/main.swift
git commit -m "feat: add TaskItem model"
```

---

### Task 3: MultiTaskResult Enum + Parser

**Files:**
- Create: `Sources/AccountaBall/Models/MultiTaskResult.swift`
- Create: `Tests/AccountaBallTests/MultiTaskResultTests.swift`
- Modify: `Tests/TestRunner/main.swift`

**Step 1: Write the failing test**

```swift
@testable import AccountaBall

func runMultiTaskResultTests() {
    suite("MultiTaskResultTests") {
        expect(MultiTaskResult.parse("TASK:0") == .onTask(index: 0), "parses TASK:0")
        expect(MultiTaskResult.parse("TASK:3") == .onTask(index: 3), "parses TASK:3")
        expect(MultiTaskResult.parse("OFFTASK") == .offTask, "parses OFFTASK")
        expect(MultiTaskResult.parse("DONE:2") == .done(index: 2), "parses DONE:2")
        expect(MultiTaskResult.parse("  task:1\n") == .onTask(index: 1), "trims whitespace")
        expect(MultiTaskResult.parse("garbage") == .offTask, "unknown defaults to offTask")
        expect(MultiTaskResult.parse("") == .offTask, "empty defaults to offTask")
        expect(MultiTaskResult.parse("JUSTIFIED") == .offTask, "non-result defaults to offTask")
    }
}
```

**Step 2: Run — expect FAIL**

**Step 3: Implement**

Create `Sources/AccountaBall/Models/MultiTaskResult.swift`:

```swift
enum MultiTaskResult: Equatable {
    case onTask(index: Int)
    case offTask
    case done(index: Int)

    static func parse(_ raw: String) -> MultiTaskResult {
        let cleaned = raw.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
        if cleaned == "OFFTASK" { return .offTask }
        if cleaned.hasPrefix("TASK:"), let idx = Int(cleaned.dropFirst(5)) {
            return .onTask(index: idx)
        }
        if cleaned.hasPrefix("DONE:"), let idx = Int(cleaned.dropFirst(5)) {
            return .done(index: idx)
        }
        return .offTask
    }
}
```

**Step 4: Run — expect PASS**

**Step 5: Commit**
```bash
git add Sources/AccountaBall/Models/MultiTaskResult.swift Tests/AccountaBallTests/MultiTaskResultTests.swift Tests/TestRunner/main.swift
git commit -m "feat: add MultiTaskResult enum with parser"
```

---

### Task 4: Update AppState for v2

**Files:**
- Modify: `Sources/AccountaBall/Models/AppState.swift` (full rewrite)
- Create: `Tests/AccountaBallTests/AppStateV2Tests.swift`
- Modify: `Tests/AccountaBallTests/AppStateTests.swift` (update broken tests)
- Modify: `Tests/TestRunner/main.swift`

**Step 1: Write the failing tests**

Create `Tests/AccountaBallTests/AppStateV2Tests.swift`:

```swift
import Foundation
@testable import AccountaBall

func runAppStateV2Tests() {
    suite("AppStateV2Tests") {
        // Initial state
        let s = AppState()
        expect(s.tasks.isEmpty, "starts with no tasks")
        expect(s.appPhase == .idle, "starts in idle phase")
        expect(s.activeTaskIndex == nil, "no active task initially")
        expect(s.sessionStartTime == nil, "no session start initially")

        // Task management
        s.tasks = [
            TaskItem(task: "write proposal", context: "for client"),
            TaskItem(task: "review slides", context: "deck for monday")
        ]
        expect(s.activeTasks.count == 2, "activeTasks excludes completed")
        s.tasks[0].isComplete = true
        expect(s.activeTasks.count == 1, "activeTasks excludes completed after marking")

        // allTasksComplete
        let s2 = AppState()
        s2.tasks = [TaskItem(task: "a", context: "b")]
        expect(!s2.allTasksComplete, "not complete when task incomplete")
        s2.tasks[0].isComplete = true
        expect(s2.allTasksComplete, "complete when all tasks done")

        // startSession
        let s3 = AppState()
        s3.tasks = [TaskItem(task: "a", context: "b")]
        s3.startSession()
        expect(s3.appPhase == .session, "startSession sets phase to session")
        expect(s3.sessionStartTime != nil, "startSession sets sessionStartTime")
        expect(s3.isCapturing == true, "startSession starts capturing")

        // completeTaskAt
        let s4 = AppState()
        s4.tasks = [TaskItem(task: "a", context: "b"), TaskItem(task: "c", context: "d")]
        s4.startSession()
        s4.completeTaskAt(index: 0)
        expect(s4.tasks[0].isComplete == true, "completeTaskAt marks task done")
        expect(s4.appPhase == .session, "completeTaskAt keeps session going if tasks remain")

        // completeTaskAt — all done triggers complete
        let s5 = AppState()
        s5.tasks = [TaskItem(task: "a", context: "b")]
        s5.startSession()
        s5.completeTaskAt(index: 0)
        expect(s5.appPhase == .complete, "completeTaskAt triggers complete when all done")

        // Persistence
        let s6 = AppState()
        let items = [TaskItem(task: "persist me", context: "ctx")]
        s6.tasks = items
        s6.saveTasks()
        let s7 = AppState()
        s7.loadTasks()
        expect(s7.tasks.first?.task == "persist me", "tasks survive save/load")
    }
}
```

Update `Tests/AccountaBallTests/AppStateTests.swift` — replace old `runAppStateTests()` entirely:

```swift
@testable import AccountaBall

func runAppStateTests() {
    // Legacy tests replaced by AppStateV2Tests
    // Kept as no-op so TestRunner compiles
}
```

**Step 2: Run — expect FAIL**

**Step 3: Implement**

Replace `Sources/AccountaBall/Models/AppState.swift`:

```swift
import Foundation
import Combine

private let tasksKey = "accountaball.tasks.v2"

class AppState: ObservableObject {
    @Published var tasks: [TaskItem] = []
    @Published var appPhase: AppPhase = .idle
    @Published var activeTaskIndex: Int? = nil
    @Published var sessionStartTime: Date? = nil
    @Published var isCapturing: Bool = false
    @Published var ballState: BallState = .idle  // kept for BallView expressions
    @Published var sessionLog: [TaskSession] = []

    var activeTasks: [TaskItem] { tasks.filter { !$0.isComplete } }
    var allTasksComplete: Bool { !tasks.isEmpty && tasks.allSatisfy { $0.isComplete } }

    func startSession() {
        sessionStartTime = Date()
        isCapturing = true
        appPhase = .session
        ballState = .onTask
    }

    func endSession() {
        isCapturing = false
        sessionStartTime = nil
        activeTaskIndex = nil
        ballState = .idle
    }

    func completeTaskAt(index: Int) {
        guard index < tasks.count else { return }
        tasks[index].isComplete = true
        if allTasksComplete {
            endSession()
            appPhase = .complete
        }
    }

    func saveTasks() {
        if let data = try? JSONEncoder().encode(tasks) {
            UserDefaults.standard.set(data, forKey: tasksKey)
        }
    }

    func loadTasks() {
        guard let data = UserDefaults.standard.data(forKey: tasksKey),
              let decoded = try? JSONDecoder().decode([TaskItem].self, from: data)
        else { return }
        tasks = decoded.map {
            var t = $0; t.isComplete = false; t.timeOnTask = 0; return t
        }
    }

    func clearSavedTasks() {
        UserDefaults.standard.removeObject(forKey: tasksKey)
        tasks = []
    }

    // Legacy compat — used by AccountabilityEngine v1 (will be replaced in Task 7)
    var currentTask: String { activeTasks.first?.task ?? "" }
}
```

**Step 4: Run — expect PASS**
```bash
make test
```

**Step 5: Commit**
```bash
git add Sources/AccountaBall/Models/AppState.swift Tests/AccountaBallTests/AppStateV2Tests.swift Tests/AccountaBallTests/AppStateTests.swift Tests/TestRunner/main.swift
git commit -m "feat: update AppState for v2 multi-task + phase management"
```

---

## Phase 2: AI Engine

### Task 5: Multi-Task Classification

**Files:**
- Modify: `Sources/AccountaBall/Services/AIService.swift` (extend protocol)
- Modify: `Sources/AccountaBall/Services/OpenRouterAIService.swift` (add classifyMulti)
- Create: `Tests/AccountaBallTests/MultiTaskClassificationTests.swift`
- Modify: `Tests/TestRunner/main.swift`

**Step 1: Write the failing test**

```swift
@testable import AccountaBall

func runMultiTaskClassificationTests() {
    suite("MultiTaskClassificationTests") {
        // Test the static prompt builder
        let tasks = [
            TaskItem(task: "write proposal", context: "for client meeting friday"),
            TaskItem(task: "review slides", context: "deck for monday presentation")
        ]
        let prompt = OpenRouterAIService.buildClassifyPrompt(tasks: tasks, screenText: "Google Docs, writing proposal outline")
        expect(prompt.contains("write proposal"), "prompt includes task 1")
        expect(prompt.contains("review slides"), "prompt includes task 2")
        expect(prompt.contains("Google Docs"), "prompt includes screen text")

        // Test response parsing
        expect(MultiTaskResult.parse("TASK:0") == .onTask(index: 0), "parses task 0")
        expect(MultiTaskResult.parse("TASK:1") == .onTask(index: 1), "parses task 1")
        expect(MultiTaskResult.parse("DONE:0") == .done(index: 0), "parses done 0")
        expect(MultiTaskResult.parse("OFFTASK") == .offTask, "parses offtask")
    }
}
```

**Step 2: Run — expect FAIL**

**Step 3: Implement**

Update `Sources/AccountaBall/Services/AIService.swift`:

```swift
protocol AIService {
    func classify(task: String, screenText: String) async throws -> BallState
    func classifyMulti(tasks: [TaskItem], screenText: String) async throws -> MultiTaskResult
    func evaluateExcuse(excuse: String, tasks: [TaskItem], screenText: String) async throws -> Bool
}
```

Update `Sources/AccountaBall/Services/OpenRouterAIService.swift` — add after `init`:

```swift
static func buildClassifyPrompt(tasks: [TaskItem], screenText: String) -> String {
    let taskList = tasks.enumerated().map { i, t in
        "TASK \(i): \(t.task)\n  Context: \(t.context)"
    }.joined(separator: "\n")
    return "Tasks:\n\(taskList)\n\nScreen text:\n\(screenText)"
}

func classifyMulti(tasks: [TaskItem], screenText: String) async throws -> MultiTaskResult {
    let systemPrompt = """
    You are an accountability assistant monitoring a user's screen.
    The user has declared the following tasks (numbered starting at 0).
    Look at the screen text and respond with EXACTLY one of:
    - TASK:N (where N is the 0-based index of the task they appear to be working on)
    - OFFTASK (if they are not working on any declared task)
    - DONE:N (if task N appears to be completed)
    Rules:
    - When uncertain, respond OFFTASK
    - Respond with ONLY the token. No explanation.
    """
    let userMessage = Self.buildClassifyPrompt(tasks: tasks, screenText: screenText)
    let raw = try await sendMessage(system: systemPrompt, user: userMessage, maxTokens: 10)
    return MultiTaskResult.parse(raw)
}

func evaluateExcuse(excuse: String, tasks: [TaskItem], screenText: String) async throws -> Bool {
    let systemPrompt = """
    You are a strict accountability judge. A user was caught off-task.
    They gave an explanation. Decide if it is legitimately necessary for their work.
    Respond with EXACTLY one word: JUSTIFIED or NOT_JUSTIFIED.
    """
    let taskList = tasks.map { "- \($0.task): \($0.context)" }.joined(separator: "\n")
    let userMessage = "Tasks:\n\(taskList)\n\nScreen text:\n\(screenText)\n\nUser's explanation:\n\(excuse)"
    let raw = try await sendMessage(system: systemPrompt, user: userMessage, maxTokens: 10)
    return raw.trimmingCharacters(in: .whitespacesAndNewlines).uppercased() == "JUSTIFIED"
}

// Shared HTTP helper
private func sendMessage(system: String, user: String, maxTokens: Int) async throws -> String {
    let body: [String: Any] = [
        "model": model,
        "max_tokens": maxTokens,
        "messages": [
            ["role": "system", "content": system],
            ["role": "user", "content": user]
        ]
    ]
    var request = URLRequest(url: endpoint)
    request.httpMethod = "POST"
    request.setValue("application/json", forHTTPHeaderField: "Content-Type")
    request.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization")
    request.httpBody = try JSONSerialization.data(withJSONObject: body)
    let (data, _) = try await URLSession.shared.data(for: request)
    guard
        let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
        let choices = json["choices"] as? [[String: Any]],
        let message = choices.first?["message"] as? [String: Any],
        let text = message["content"] as? String
    else { return "" }
    return text
}
```

Also update `classify(task:screenText:)` in `OpenRouterAIService` to call `sendMessage` instead of duplicating HTTP code:

```swift
func classify(task: String, screenText: String) async throws -> BallState {
    let system = "You are an accountability assistant. Respond with exactly one word: ONTASK, OFFTASK, or DONE. No explanation."
    let raw = try await sendMessage(system: system, user: "Task: \(task)\n\nScreen text:\n\(screenText)", maxTokens: 10)
    return ClaudeAIService.parseResponse(raw)
}
```

Add stub implementations to `ClaudeAIService` so it still compiles:

```swift
func classifyMulti(tasks: [TaskItem], screenText: String) async throws -> MultiTaskResult {
    let result = try await classify(task: tasks.first?.task ?? "", screenText: screenText)
    switch result {
    case .onTask: return .onTask(index: 0)
    case .offTask: return .offTask
    case .done:   return .done(index: 0)
    case .idle:   return .offTask
    }
}

func evaluateExcuse(excuse: String, tasks: [TaskItem], screenText: String) async throws -> Bool {
    return true  // stub — OpenRouterAIService has the real impl
}
```

**Step 4: Run — expect PASS**
```bash
make test
```

**Step 5: Commit**
```bash
git add Sources/AccountaBall/Services/AIService.swift Sources/AccountaBall/Services/OpenRouterAIService.swift Sources/AccountaBall/Services/ClaudeAIService.swift Tests/AccountaBallTests/MultiTaskClassificationTests.swift Tests/TestRunner/main.swift
git commit -m "feat: add classifyMulti and evaluateExcuse to AIService"
```

---

### Task 6: Update AccountabilityEngine for Multi-Task

**Files:**
- Modify: `Sources/AccountaBall/Engine/AccountabilityEngine.swift` (full rewrite)
- Modify: `Tests/AccountaBallTests/AccountabilityEngineTests.swift` (update for new init)

**Step 1: Rewrite AccountabilityEngine**

Replace `Sources/AccountaBall/Engine/AccountabilityEngine.swift`:

```swift
import Foundation

@MainActor
class AccountabilityEngine {
    private let state: AppState
    private let captureService: ScreenCaptureService
    private let ocrService: OCRService
    private let aiService: AIService
    private let notificationService: NotificationService
    private var captureTask: Task<Void, Never>?
    private var suspicionCount: Int = 0
    private var lastOCRText: String = ""

    init(
        state: AppState,
        captureService: ScreenCaptureService,
        ocrService: OCRService,
        aiService: AIService,
        notificationService: NotificationService
    ) {
        self.state = state
        self.captureService = captureService
        self.ocrService = ocrService
        self.aiService = aiService
        self.notificationService = notificationService
    }

    func start() {
        captureTask = captureService.startLoop(interval: 5, panelTitle: "AccountaBall") { [weak self] image in
            guard let self else { return }
            let text = await self.ocrService.extractText(from: image)
            guard !text.isEmpty else { return }
            self.lastOCRText = text
            let activeTasks = self.state.activeTasks
            guard !activeTasks.isEmpty else { return }
            let result = (try? await self.aiService.classifyMulti(tasks: activeTasks, screenText: text)) ?? .offTask
            self.processResult(result)
        }
    }

    func stop() {
        captureTask?.cancel()
        captureTask = nil
        suspicionCount = 0
    }

    var lastScreenText: String { lastOCRText }

    func processResult(_ result: MultiTaskResult) {
        switch result {
        case .onTask(let index):
            suspicionCount = 0
            state.activeTaskIndex = index
            state.ballState = .onTask
            // Accumulate time on this task
            state.tasks[index].timeOnTask += 5  // 5s per cycle
        case .offTask:
            suspicionCount += 1
            state.activeTaskIndex = nil
            if suspicionCount >= 2 {
                state.ballState = .offTask
                state.appPhase = .offTask
                notificationService.sendOffTaskNudge(task: state.activeTasks.first?.task ?? "")
            }
        case .done(let index):
            stop()
            state.completeTaskAt(index: index)
        }
    }

    func resumeAfterExcuse() {
        suspicionCount = 0
        state.ballState = .onTask
        state.appPhase = .session
    }
}
```

**Step 2: Update AccountabilityEngineTests**

Update `Tests/AccountaBallTests/AccountabilityEngineTests.swift` — replace `MockAI` with `MultiMockAI` and update test assertions:

```swift
@testable import AccountaBall

private class MultiMockAI: AIService {
    var nextResult: MultiTaskResult = .offTask
    func classify(task: String, screenText: String) async throws -> BallState { .onTask }
    func classifyMulti(tasks: [TaskItem], screenText: String) async throws -> MultiTaskResult { nextResult }
    func evaluateExcuse(excuse: String, tasks: [TaskItem], screenText: String) async throws -> Bool { true }
}

@MainActor
func runAccountabilityEngineTests() {
    let capture = ScreenCaptureService()
    let ocr = OCRService()
    let notif = NotificationService()
    let mockAI = MultiMockAI()

    func make() -> (AppState, AccountabilityEngine) {
        let s = AppState()
        s.tasks = [TaskItem(task: "write proposal", context: "for client"), TaskItem(task: "review slides", context: "deck")]
        s.startSession()
        let e = AccountabilityEngine(state: s, captureService: capture, ocrService: ocr, aiService: mockAI, notificationService: notif)
        return (s, e)
    }

    suite("AccountabilityEngineTests") {
        var (state, engine) = make()
        engine.processResult(.offTask)
        expect(state.ballState == .onTask, "single offTask does not flip state")

        (state, engine) = make()
        engine.processResult(.offTask)
        engine.processResult(.offTask)
        expect(state.ballState == .offTask, "two consecutive offTask flips to offTask")
        expect(state.appPhase == .offTask, "two consecutive offTask sets offTask phase")

        (state, engine) = make()
        engine.processResult(.offTask)
        engine.processResult(.onTask(index: 0))
        engine.processResult(.offTask)
        expect(state.ballState == .onTask, "onTask resets suspicion")

        (state, engine) = make()
        engine.processResult(.onTask(index: 0))
        expect(state.activeTaskIndex == 0, "onTask sets activeTaskIndex")
        expect(state.tasks[0].timeOnTask == 5, "onTask accumulates 5s per cycle")

        (state, engine) = make()
        engine.processResult(.done(index: 0))
        expect(state.tasks[0].isComplete == true, "done marks task complete")

        (state, engine) = make()
        engine.processResult(.done(index: 0))
        engine.processResult(.done(index: 1))
        expect(state.appPhase == .complete, "all done triggers complete phase")
    }
}
```

**Step 3: Run — expect PASS**
```bash
make test
```

**Step 4: Commit**
```bash
git add Sources/AccountaBall/Engine/AccountabilityEngine.swift Tests/AccountaBallTests/AccountabilityEngineTests.swift
git commit -m "feat: update AccountabilityEngine for multi-task classification + per-task time"
```

---

## Phase 3: Welcome + Setup Views

### Task 7: BasketballView (Reusable Graphic)

**Files:**
- Create: `Sources/AccountaBall/Views/BasketballView.swift`

No unit test — visual. Verify by running the app.

**Step 1: Create BasketballView.swift**

```swift
import SwiftUI

struct BasketballView: View {
    var size: CGFloat = 60
    var showFace: BallFace = .none
    var rotation: Double = 0

    enum BallFace {
        case none, happy, angry
    }

    var body: some View {
        ZStack {
            // Base orange circle
            Circle()
                .fill(Color.orange)
                .overlay(Circle().stroke(Color.black.opacity(0.15), lineWidth: 1))

            // Basketball seam lines
            basketballSeams

            // Face overlay
            if showFace != .none {
                faceOverlay
            }
        }
        .frame(width: size, height: size)
        .rotationEffect(.degrees(rotation))
    }

    private var basketballSeams: some View {
        Canvas { ctx, size in
            let w = size.width, h = size.height
            var path = Path()
            // Horizontal equator
            path.move(to: CGPoint(x: 0, y: h / 2))
            path.addLine(to: CGPoint(x: w, y: h / 2))
            // Vertical center
            path.move(to: CGPoint(x: w / 2, y: 0))
            path.addLine(to: CGPoint(x: w / 2, y: h))
            // Left curve
            path.move(to: CGPoint(x: w * 0.25, y: 0))
            path.addCurve(to: CGPoint(x: w * 0.25, y: h),
                          control1: CGPoint(x: w * 0.5, y: h * 0.3),
                          control2: CGPoint(x: w * 0.5, y: h * 0.7))
            // Right curve
            path.move(to: CGPoint(x: w * 0.75, y: 0))
            path.addCurve(to: CGPoint(x: w * 0.75, y: h),
                          control1: CGPoint(x: w * 0.5, y: h * 0.3),
                          control2: CGPoint(x: w * 0.5, y: h * 0.7))
            ctx.stroke(path, with: .color(.black.opacity(0.7)), lineWidth: max(1.5, size.width / 30))
        }
    }

    private var faceOverlay: some View {
        VStack(spacing: size * 0.08) {
            HStack(spacing: size * 0.2) {
                Circle().fill(.black).frame(width: size * 0.12, height: size * 0.12)
                Circle().fill(.black).frame(width: size * 0.12, height: size * 0.12)
            }
            .offset(y: -size * 0.05)

            Path { path in
                if showFace == .happy {
                    path.addArc(center: CGPoint(x: size * 0.17, y: 0),
                                radius: size * 0.17,
                                startAngle: .degrees(0), endAngle: .degrees(180), clockwise: false)
                } else {
                    path.addArc(center: CGPoint(x: size * 0.17, y: size * 0.1),
                                radius: size * 0.17,
                                startAngle: .degrees(0), endAngle: .degrees(180), clockwise: true)
                }
            }
            .stroke(Color.black, lineWidth: max(1.5, size / 30))
            .frame(width: size * 0.34, height: size * 0.2)
        }
    }
}
```

**Step 2: Commit**
```bash
git add Sources/AccountaBall/Views/BasketballView.swift
git commit -m "feat: add BasketballView reusable component"
```

---

### Task 8: WelcomeView with Bounce Animation

**Files:**
- Create: `Sources/AccountaBall/Views/WelcomeView.swift`

No unit test — visual.

**Step 1: Create WelcomeView.swift**

```swift
import SwiftUI

struct WelcomeView: View {
    @EnvironmentObject var state: AppState
    @State private var bouncePhase = 0
    @State private var showContent = false
    @State private var ballY: CGFloat = -150
    @State private var squish: CGFloat = 1.0

    var body: some View {
        ZStack {
            Color.black.opacity(0.85).ignoresSafeArea()

            VStack(spacing: 24) {
                Spacer()

                ZStack(alignment: .top) {
                    if showContent {
                        SpeechBubble(text: "Ready to finish your tasks?")
                            .offset(y: -90)
                            .transition(.opacity.combined(with: .scale(scale: 0.8)))
                    }

                    BasketballView(size: 80, showFace: .happy)
                        .scaleEffect(x: squish > 1 ? squish : 1, y: squish < 1 ? squish : 1)
                        .offset(y: ballY)
                        .onAppear { runBounce() }
                }
                .frame(height: 200)

                if showContent {
                    VStack(spacing: 12) {
                        if !state.tasks.isEmpty {
                            Text("Welcome back — you have \(state.tasks.count) task\(state.tasks.count == 1 ? "" : "s") from last time")
                                .font(.caption)
                                .foregroundStyle(.white.opacity(0.6))

                            Button("start fresh") { state.clearSavedTasks() }
                                .font(.caption)
                                .foregroundStyle(.orange)
                                .buttonStyle(.plain)
                        }

                        Button("Let's get started") {
                            withAnimation { state.appPhase = .setup }
                        }
                        .buttonStyle(PrimaryButtonStyle())
                    }
                    .transition(.opacity.combined(with: .move(edge: .bottom)))
                }

                Spacer()
            }
            .padding(32)
        }
    }

    private func runBounce() {
        // Phase 1: drop from top
        withAnimation(.easeIn(duration: 0.3)) { ballY = 0 }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
            withAnimation(.easeOut(duration: 0.05)) { squish = 0.7 }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.05) {
                withAnimation(.easeIn(duration: 0.05)) { squish = 1.0 }
                // Phase 2: bounce up 60%
                withAnimation(.easeOut(duration: 0.25)) { ballY = -90 }
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) {
                    withAnimation(.easeIn(duration: 0.2)) { ballY = 0 }
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) {
                        withAnimation(.easeOut(duration: 0.05)) { squish = 0.8 }
                        DispatchQueue.main.asyncAfter(deadline: .now() + 0.05) {
                            withAnimation(.easeIn(duration: 0.05)) { squish = 1.0 }
                            // Phase 3: small bounce 30%
                            withAnimation(.easeOut(duration: 0.18)) { ballY = -45 }
                            DispatchQueue.main.asyncAfter(deadline: .now() + 0.18) {
                                withAnimation(.spring(duration: 0.15, bounce: 0.3)) { ballY = 0 }
                                DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
                                    withAnimation(.spring(duration: 0.4)) { showContent = true }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

struct SpeechBubble: View {
    let text: String

    var body: some View {
        VStack(spacing: 0) {
            Text(text)
                .font(.system(size: 14, weight: .medium))
                .foregroundStyle(.black)
                .padding(.horizontal, 14)
                .padding(.vertical, 10)
                .background(Color.white)
                .clipShape(RoundedRectangle(cornerRadius: 12))
                .shadow(color: .black.opacity(0.15), radius: 4, y: 2)

            // Pointer triangle pointing down
            Triangle()
                .fill(Color.white)
                .frame(width: 16, height: 8)
        }
    }
}

struct Triangle: Shape {
    func path(in rect: CGRect) -> Path {
        Path { p in
            p.move(to: CGPoint(x: rect.midX, y: rect.maxY))
            p.addLine(to: CGPoint(x: rect.minX, y: rect.minY))
            p.addLine(to: CGPoint(x: rect.maxX, y: rect.minY))
            p.closeSubpath()
        }
    }
}

struct PrimaryButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 15, weight: .semibold))
            .foregroundStyle(.black)
            .padding(.horizontal, 28)
            .padding(.vertical, 12)
            .background(Color.orange)
            .clipShape(RoundedRectangle(cornerRadius: 10))
            .scaleEffect(configuration.isPressed ? 0.96 : 1)
    }
}
```

**Step 2: Commit**
```bash
git add Sources/AccountaBall/Views/WelcomeView.swift
git commit -m "feat: add WelcomeView with 3-phase bounce animation and speech bubble"
```

---

### Task 9: TaskSetupView

**Files:**
- Create: `Sources/AccountaBall/Views/TaskSetupView.swift`

No unit test — visual. Validation logic is tested via AppState.

**Step 1: Create TaskSetupView.swift**

```swift
import SwiftUI

struct TaskSetupView: View {
    @EnvironmentObject var state: AppState
    @State private var showValidationError = false

    private let maxRows = 5

    var body: some View {
        ZStack {
            Color.black.opacity(0.85).ignoresSafeArea()

            VStack(spacing: 0) {
                // Header with mini ball
                HStack {
                    BasketballView(size: 36, showFace: .happy)
                    Text("What are we conquering today?")
                        .font(.system(size: 15, weight: .semibold))
                        .foregroundStyle(.white)
                    Spacer()
                }
                .padding(.horizontal, 20)
                .padding(.top, 20)
                .padding(.bottom, 16)

                // Column headers
                HStack(spacing: 12) {
                    Text("Task").frame(maxWidth: .infinity, alignment: .leading)
                    Text("Context").frame(maxWidth: .infinity, alignment: .leading)
                }
                .font(.caption)
                .foregroundStyle(.white.opacity(0.5))
                .padding(.horizontal, 20)
                .padding(.bottom, 8)

                // Rows
                VStack(spacing: 8) {
                    ForEach(0..<maxRows, id: \.self) { i in
                        TaskRowView(
                            index: i,
                            isUnlocked: isRowUnlocked(i),
                            showError: showValidationError
                        )
                    }
                }
                .padding(.horizontal, 20)

                Spacer()

                // Let's go button
                Button("Let's go!") { handleLetsGo() }
                    .buttonStyle(PrimaryButtonStyle())
                    .disabled(!hasAtLeastOneCompleteRow)
                    .opacity(hasAtLeastOneCompleteRow ? 1 : 0.4)
                    .padding(.bottom, 24)
            }
        }
        .onAppear {
            // Ensure at least 1 empty row exists
            if state.tasks.isEmpty {
                state.tasks = Array(repeating: TaskItem(), count: maxRows)
            }
        }
    }

    private func isRowUnlocked(_ i: Int) -> Bool {
        if i == 0 { return true }
        return state.tasks.indices.contains(i - 1) && state.tasks[i - 1].isFilledIn
    }

    private var hasAtLeastOneCompleteRow: Bool {
        state.tasks.contains { $0.isFilledIn }
    }

    private func handleLetsGo() {
        // Remove empty rows before saving
        let filled = state.tasks.filter { $0.isFilledIn }
        guard !filled.isEmpty else { showValidationError = true; return }
        state.tasks = filled
        state.saveTasks()
        withAnimation { state.appPhase = .session }
        state.startSession()
    }
}

struct TaskRowView: View {
    @EnvironmentObject var state: AppState
    let index: Int
    let isUnlocked: Bool
    let showError: Bool

    private var task: Binding<String> {
        Binding(
            get: { state.tasks.indices.contains(index) ? state.tasks[index].task : "" },
            set: { if state.tasks.indices.contains(index) { state.tasks[index].task = $0; state.saveTasks() } }
        )
    }

    private var context: Binding<String> {
        Binding(
            get: { state.tasks.indices.contains(index) ? state.tasks[index].context : "" },
            set: { if state.tasks.indices.contains(index) { state.tasks[index].context = $0; state.saveTasks() } }
        )
    }

    private var taskEmpty: Bool { (state.tasks.indices.contains(index) ? state.tasks[index].task : "").isEmpty }
    private var contextEmpty: Bool { (state.tasks.indices.contains(index) ? state.tasks[index].context : "").isEmpty }

    var body: some View {
        HStack(spacing: 12) {
            RowField(text: task, placeholder: "Task \(index + 1)", showError: showError && isUnlocked && taskEmpty)
            RowField(text: context, placeholder: "Context", showError: showError && isUnlocked && contextEmpty)
        }
        .opacity(isUnlocked ? 1 : 0.3)
        .disabled(!isUnlocked)
    }
}

struct RowField: View {
    @Binding var text: String
    let placeholder: String
    let showError: Bool

    var body: some View {
        TextField(placeholder, text: $text)
            .textFieldStyle(.plain)
            .font(.system(size: 13))
            .foregroundStyle(.white)
            .padding(.horizontal, 10)
            .padding(.vertical, 8)
            .background(
                RoundedRectangle(cornerRadius: 8)
                    .fill(Color.white.opacity(0.1))
                    .overlay(
                        RoundedRectangle(cornerRadius: 8)
                            .stroke(showError ? Color.red.opacity(0.7) : Color.clear, lineWidth: 1)
                    )
            )
            .frame(maxWidth: .infinity)
    }
}
```

**Step 2: Commit**
```bash
git add Sources/AccountaBall/Views/TaskSetupView.swift
git commit -m "feat: add TaskSetupView with 5-row table, row unlocking, autosave"
```

---

## Phase 4: Session + Off-Task Views

### Task 10: SessionBallView (Edge Widget)

**Files:**
- Create: `Sources/AccountaBall/Views/SessionBallView.swift`

No unit test — visual.

**Step 1: Create SessionBallView.swift**

```swift
import SwiftUI

struct SessionBallView: View {
    @EnvironmentObject var state: AppState
    var onTap: () -> Void
    var onOffTaskDismiss: () -> Void

    @State private var yOffset: CGFloat = 100
    @State private var pulsing = false
    @State private var sessionSeconds: Int = 0
    private let timer = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    var body: some View {
        ZStack {
            // Basketball (half off screen to the right)
            HStack {
                Spacer()
                ZStack {
                    BasketballView(size: 80)

                    // Timer overlay on visible half
                    VStack(spacing: 2) {
                        Text(formatTime(sessionSeconds))
                            .font(.system(size: 11, weight: .bold, design: .monospaced))
                            .foregroundStyle(.white)

                        if let idx = state.activeTaskIndex, state.tasks.indices.contains(idx) {
                            Divider().frame(width: 50).overlay(.white.opacity(0.5))
                            Text("Task \(idx + 1)")
                                .font(.system(size: 9, weight: .medium))
                                .foregroundStyle(.white.opacity(0.8))
                            Text(formatTime(Int(state.tasks[idx].timeOnTask)))
                                .font(.system(size: 10, weight: .bold, design: .monospaced))
                                .foregroundStyle(.white)
                        }
                    }
                    .frame(width: 50)
                }
                .frame(width: 80)
                .offset(x: 40)  // half off screen
                .offset(y: yOffset)
                .scaleEffect(pulsing ? 1.05 : 1.0)
                .onTapGesture { onTap() }
                .gesture(DragGesture().onChanged { v in yOffset += v.translation.height })
            }
        }
        .ignoresSafeArea()
        .onReceive(timer) { _ in
            sessionSeconds += 1
            // Gentle pulse every 4s
            if sessionSeconds % 4 == 0 {
                withAnimation(.easeInOut(duration: 0.3)) { pulsing = true }
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
                    withAnimation(.easeInOut(duration: 0.3)) { pulsing = false }
                }
            }
        }
    }

    private func formatTime(_ seconds: Int) -> String {
        let h = seconds / 3600, m = (seconds % 3600) / 60, s = seconds % 60
        return h > 0 ? String(format: "%02d:%02d:%02d", h, m, s) : String(format: "%02d:%02d", m, s)
    }
}
```

**Step 2: Commit**
```bash
git add Sources/AccountaBall/Views/SessionBallView.swift
git commit -m "feat: add SessionBallView — edge widget with session + task timers"
```

---

### Task 11: OffTaskView (Angry Creep + Excuse)

**Files:**
- Create: `Sources/AccountaBall/Views/OffTaskView.swift`

No unit test — visual.

**Step 1: Create OffTaskView.swift**

```swift
import SwiftUI

struct OffTaskView: View {
    @EnvironmentObject var state: AppState
    var engine: AccountabilityEngine
    var aiService: AIService
    var lastScreenText: String

    @State private var slideIn: CGFloat = 0   // 0=edge, 1=1/3 in
    @State private var rotated = false
    @State private var excuseText = ""
    @State private var message: String? = nil
    @State private var isEvaluating = false
    @State private var timeoutTask: Task<Void, Never>? = nil

    var body: some View {
        HStack {
            Spacer()

            VStack(alignment: .trailing, spacing: 0) {
                // Speech bubble
                if let msg = message {
                    SpeechBubble(text: msg)
                        .padding(.trailing, 90)
                        .transition(.opacity)
                } else {
                    SpeechBubble(text: "What are you doing?")
                        .padding(.trailing, 90)
                }

                // Ball with rotation (timer back → angry face front)
                BasketballView(size: 80, showFace: rotated ? .angry : .none)
                    .rotation3DEffect(.degrees(rotated ? 0 : 180), axis: (0, 1, 0))
                    .padding(.trailing, 40 - (slideIn * 100))

                // Input
                if rotated && message == nil {
                    VStack(spacing: 8) {
                        TextField("What are you doing?", text: $excuseText)
                            .textFieldStyle(.plain)
                            .foregroundStyle(.white)
                            .padding(10)
                            .background(Color.white.opacity(0.15))
                            .clipShape(RoundedRectangle(cornerRadius: 8))
                            .frame(width: 220)
                            .onSubmit { submitExcuse() }

                        Button(isEvaluating ? "Checking..." : "Submit") { submitExcuse() }
                            .buttonStyle(PrimaryButtonStyle())
                            .disabled(excuseText.isEmpty || isEvaluating)
                    }
                    .padding(.trailing, 50)
                    .transition(.opacity.combined(with: .move(edge: .trailing)))
                }
            }
        }
        .ignoresSafeArea()
        .onAppear { startCreep() }
    }

    private func startCreep() {
        // Start 60s timeout
        timeoutTask = Task { @MainActor in
            try? await Task.sleep(for: .seconds(60))
            guard state.appPhase == .offTask else { return }
            engine.resumeAfterExcuse()
        }
        // Animate rotation then slide in
        withAnimation(.easeInOut(duration: 0.6)) { rotated = true }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
            withAnimation(.easeInOut(duration: 1.5)) { slideIn = 1 }
        }
    }

    private func submitExcuse() {
        guard !excuseText.isEmpty else { return }
        timeoutTask?.cancel()
        isEvaluating = true
        Task { @MainActor in
            let justified = (try? await aiService.evaluateExcuse(
                excuse: excuseText,
                tasks: state.activeTasks,
                screenText: lastScreenText
            )) ?? false
            withAnimation {
                message = justified ? "Okay, carry on 👍" : "Get back to work."
            }
            try? await Task.sleep(for: .seconds(2))
            if justified {
                engine.resumeAfterExcuse()
            } else {
                // Slide back but keep angry for one cycle
                withAnimation(.easeInOut(duration: 0.8)) { slideIn = 0 }
                try? await Task.sleep(for: .seconds(0.8))
                engine.resumeAfterExcuse()
            }
        }
    }
}
```

**Step 2: Commit**
```bash
git add Sources/AccountaBall/Views/OffTaskView.swift
git commit -m "feat: add OffTaskView — angry creep animation with AI-judged excuse"
```

---

## Phase 5: Progress + Completion

### Task 12: WhatsUpView + ProgressView

**Files:**
- Create: `Sources/AccountaBall/Views/ProgressView.swift`

Note: `ProgressView` is a name conflict with SwiftUI — name the file `ProgressView.swift` but the struct `AccountaProgressView`.

**Step 1: Create Sources/AccountaBall/Views/AccountaProgressView.swift**

```swift
import SwiftUI

struct WhatsUpView: View {
    @EnvironmentObject var state: AppState
    var onDismiss: () -> Void

    @State private var showProgress = false

    var body: some View {
        ZStack {
            if showProgress {
                AccountaProgressView(onBack: { showProgress = false; onDismiss() })
                    .transition(.opacity)
            } else {
                VStack(spacing: 20) {
                    SpeechBubble(text: "What's up?")
                    BasketballView(size: 80, showFace: .happy)

                    HStack(spacing: 16) {
                        Button("Show my progress") {
                            withAnimation { showProgress = true }
                        }
                        .buttonStyle(PrimaryButtonStyle())

                        Button("Nothing") { onDismiss() }
                            .font(.system(size: 14))
                            .foregroundStyle(.white.opacity(0.6))
                            .buttonStyle(.plain)
                    }
                }
                .padding(32)
                .background(Color.black.opacity(0.85))
                .clipShape(RoundedRectangle(cornerRadius: 20))
                .transition(.opacity.combined(with: .scale(scale: 0.9)))
            }
        }
    }
}

struct AccountaProgressView: View {
    @EnvironmentObject var state: AppState
    var onBack: () -> Void

    var body: some View {
        VStack(spacing: 0) {
            Color.black.opacity(0.85).ignoresSafeArea()

            VStack(alignment: .leading, spacing: 16) {
                // Session timer
                HStack {
                    Image(systemName: "timer")
                    Text("Session time")
                    Spacer()
                    Text(sessionDuration)
                        .font(.system(.body, design: .monospaced).weight(.bold))
                }
                .foregroundStyle(.white)

                Divider().overlay(.white.opacity(0.2))

                // Task list
                HStack {
                    Text("Task").frame(maxWidth: .infinity, alignment: .leading)
                    Text("Time").frame(width: 80, alignment: .trailing)
                    Text("Done").frame(width: 44, alignment: .center)
                }
                .font(.caption)
                .foregroundStyle(.white.opacity(0.5))

                ForEach(state.tasks.indices, id: \.self) { i in
                    let task = state.tasks[i]
                    HStack {
                        Text(task.task)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .strikethrough(task.isComplete)
                            .foregroundStyle(task.isComplete ? .white.opacity(0.3) : .white)

                        Text(formatTime(Int(task.timeOnTask)))
                            .frame(width: 80, alignment: .trailing)
                            .font(.system(.caption, design: .monospaced))
                            .foregroundStyle(.white.opacity(0.7))

                        Button {
                            state.completeTaskAt(index: i)
                        } label: {
                            Image(systemName: task.isComplete ? "checkmark.circle.fill" : "circle")
                                .foregroundStyle(task.isComplete ? .orange : .white.opacity(0.4))
                        }
                        .buttonStyle(.plain)
                        .frame(width: 44)
                    }
                }

                Spacer()
                Button("Back to work") { onBack() }
                    .buttonStyle(PrimaryButtonStyle())
                    .frame(maxWidth: .infinity)
            }
            .padding(24)
        }
    }

    private var sessionDuration: String {
        guard let start = state.sessionStartTime else { return "00:00" }
        return formatTime(Int(Date().timeIntervalSince(start)))
    }

    private func formatTime(_ seconds: Int) -> String {
        let h = seconds / 3600, m = (seconds % 3600) / 60, s = seconds % 60
        return h > 0 ? String(format: "%02d:%02d:%02d", h, m, s) : String(format: "%02d:%02d", m, s)
    }
}
```

**Step 2: Commit**
```bash
git add Sources/AccountaBall/Views/AccountaProgressView.swift
git commit -m "feat: add WhatsUpView and AccountaProgressView with checkboxes"
```

---

### Task 13: CompletionView (3-Point Shot)

**Files:**
- Create: `Sources/AccountaBall/Views/CompletionView.swift`

**Step 1: Create CompletionView.swift**

```swift
import SwiftUI

struct CompletionView: View {
    @EnvironmentObject var state: AppState
    @State private var animationPhase = 0
    @State private var ballX: CGFloat = 0.8   // fraction of screen width
    @State private var ballY: CGFloat = 0.6
    @State private var ballSize: CGFloat = 40
    @State private var ballRotation: Double = 0
    @State private var showHoop = false
    @State private var netDrop: CGFloat = 0
    @State private var confetti: [ConfettiPiece] = []
    @State private var showSummary = false

    var onRestart: () -> Void
    var onQuit: () -> Void

    var body: some View {
        GeometryReader { geo in
            ZStack {
                Color.black.opacity(0.92).ignoresSafeArea()

                // Hoop (left side)
                if showHoop {
                    HoopView(netDrop: netDrop)
                        .position(x: geo.size.width * 0.12, y: geo.size.height * 0.4)
                        .transition(.opacity)
                }

                // Confetti
                ForEach(confetti) { piece in
                    Circle()
                        .fill(piece.color)
                        .frame(width: piece.size, height: piece.size)
                        .position(x: piece.x, y: piece.y)
                        .opacity(piece.opacity)
                }

                // Basketball
                if animationPhase < 3 {
                    BasketballView(size: ballSize)
                        .rotationEffect(.degrees(ballRotation))
                        .position(x: geo.size.width * ballX, y: geo.size.height * ballY)
                }

                // Summary
                if showSummary {
                    VStack(spacing: 24) {
                        Text("🏀")
                            .font(.system(size: 60))
                        Text("Session Complete!")
                            .font(.system(size: 28, weight: .bold))
                            .foregroundStyle(.white)

                        VStack(spacing: 8) {
                            StatRow(label: "Total time", value: sessionDuration)
                            StatRow(label: "Tasks finished", value: "\(state.tasks.count) / \(state.tasks.count)")
                        }
                        .padding(20)
                        .background(Color.white.opacity(0.08))
                        .clipShape(RoundedRectangle(cornerRadius: 12))

                        VStack(spacing: 12) {
                            Button("Start a new session") { onRestart() }
                                .buttonStyle(PrimaryButtonStyle())
                            Button("Quit") { onQuit() }
                                .font(.system(size: 14))
                                .foregroundStyle(.white.opacity(0.5))
                                .buttonStyle(.plain)
                        }
                    }
                    .padding(40)
                    .transition(.opacity.combined(with: .scale(scale: 0.9)))
                }
            }
            .onAppear { runAnimation(in: geo.size) }
        }
    }

    private var sessionDuration: String {
        guard let start = state.sessionStartTime else { return "--:--" }
        let s = Int(Date().timeIntervalSince(start))
        let h = s / 3600, m = (s % 3600) / 60, sec = s % 60
        return h > 0 ? String(format: "%d:%02d:%02d", h, m, sec) : String(format: "%02d:%02d", m, sec)
    }

    private func runAnimation(in size: CGSize) {
        // Shoot the ball: right side → arc → left hoop
        let duration = 1.2
        withAnimation(.easeOut(duration: 0.2)) { showHoop = true }
        withAnimation(.timingCurve(0.17, 0.67, 0.35, 1.0, duration: duration)) {
            ballX = 0.12; ballY = 0.4; ballSize = 30
        }
        withAnimation(.linear(duration: duration)) { ballRotation = -720 }

        DispatchQueue.main.asyncAfter(deadline: .now() + duration) {
            // Swish
            withAnimation(.easeOut(duration: 0.3)) { netDrop = 20 }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
                withAnimation(.spring()) { netDrop = 0 }
                // Confetti burst
                confetti = (0..<30).map { _ in
                    ConfettiPiece(
                        x: size.width * 0.12 + CGFloat.random(in: -100...100),
                        y: size.height * 0.4 + CGFloat.random(in: -60...60),
                        color: [.orange, .white, .yellow, .red, .blue].randomElement()!,
                        size: CGFloat.random(in: 6...14),
                        opacity: 1
                    )
                }
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                    withAnimation(.easeOut(duration: 0.6)) {
                        confetti = confetti.map { var p = $0; p.opacity = 0; return p }
                    }
                }
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                    withAnimation(.spring(duration: 0.5)) { showSummary = true }
                    animationPhase = 3
                }
            }
        }
    }
}

struct ConfettiPiece: Identifiable {
    let id = UUID()
    var x, y: CGFloat
    var color: Color
    var size: CGFloat
    var opacity: Double
}

struct HoopView: View {
    var netDrop: CGFloat

    var body: some View {
        ZStack(alignment: .top) {
            // Rim
            Circle()
                .stroke(Color.orange, lineWidth: 4)
                .frame(width: 50, height: 50)

            // Net
            Canvas { ctx, size in
                let netLines = 6
                for i in 0..<netLines {
                    let x = size.width * CGFloat(i) / CGFloat(netLines - 1)
                    var path = Path()
                    path.move(to: CGPoint(x: x, y: 0))
                    path.addLine(to: CGPoint(x: size.width / 2, y: size.height + netDrop))
                    ctx.stroke(path, with: .color(.white.opacity(0.6)), lineWidth: 1)
                }
            }
            .frame(width: 50, height: 30)
            .offset(y: 25)
        }
    }
}

struct StatRow: View {
    let label: String, value: String
    var body: some View {
        HStack {
            Text(label).foregroundStyle(.white.opacity(0.6))
            Spacer()
            Text(value).foregroundStyle(.white).fontWeight(.semibold)
        }
    }
}
```

**Step 2: Commit**
```bash
git add Sources/AccountaBall/Views/CompletionView.swift
git commit -m "feat: add CompletionView with 3-point arc animation, hoop, confetti"
```

---

## Phase 6: Wire Everything Together

### Task 14: RootCoordinatorView + AppDelegate Update

**Files:**
- Create: `Sources/AccountaBall/Views/RootCoordinatorView.swift`
- Modify: `Sources/AccountaBall/AppDelegate.swift`
- Modify: `Sources/AccountaBall/Views/FloatingPanel.swift` (dynamic sizing)

**Step 1: Create RootCoordinatorView.swift**

```swift
import SwiftUI

struct RootCoordinatorView: View {
    @EnvironmentObject var state: AppState
    var engine: AccountabilityEngine
    var aiService: AIService

    @State private var showWhatsUp = false

    var body: some View {
        ZStack {
            switch state.appPhase {
            case .idle, .welcome:
                WelcomeView()
                    .transition(.opacity)

            case .setup:
                TaskSetupView()
                    .transition(.opacity)

            case .session:
                SessionBallView(
                    onTap: { withAnimation { showWhatsUp = true } },
                    onOffTaskDismiss: {}
                )
                .transition(.opacity)
                .overlay {
                    if showWhatsUp {
                        WhatsUpView(onDismiss: { withAnimation { showWhatsUp = false } })
                            .transition(.opacity.combined(with: .scale(scale: 0.9)))
                    }
                }

            case .offTask:
                SessionBallView(onTap: {}, onOffTaskDismiss: {})
                    .overlay(alignment: .trailing) {
                        OffTaskView(
                            engine: engine,
                            aiService: aiService,
                            lastScreenText: engine.lastScreenText
                        )
                        .transition(.opacity)
                    }

            case .progress:
                AccountaProgressView(onBack: { withAnimation { state.appPhase = .session } })
                    .transition(.opacity)

            case .complete:
                CompletionView(
                    onRestart: {
                        state.loadTasks()
                        withAnimation { state.appPhase = .welcome }
                    },
                    onQuit: { NSApp.terminate(nil) }
                )
                .transition(.opacity)
            }
        }
        .animation(.easeInOut(duration: 0.3), value: state.appPhase)
        .onAppear {
            if state.appPhase == .idle {
                state.loadTasks()
                withAnimation { state.appPhase = .welcome }
            }
        }
    }
}
```

**Step 2: Update FloatingPanel.swift** — add dynamic resize method:

```swift
func resize(for phase: AppPhase) {
    let size: NSSize
    switch phase {
    case .idle, .welcome:        size = NSSize(width: 400, height: 320)
    case .setup:                 size = NSSize(width: 520, height: 440)
    case .session, .offTask:     size = NSSize(width: 0, height: 0)  // transparent overlay
    case .progress:              size = NSSize(width: 480, height: 420)
    case .complete:              size = NSSize(width: 600, height: 500)
    }
    if phase == .session || phase == .offTask {
        // Full screen transparent overlay for edge widget
        if let screen = NSScreen.main {
            setFrame(screen.frame, display: true, animate: true)
        }
    } else {
        setContentSize(size)
        center()
    }
}
```

**Step 3: Update AppDelegate.swift** — wire RootCoordinatorView:

Replace the `applicationDidFinishLaunching` content:

```swift
public func applicationDidFinishLaunching(_ notification: Notification) {
    NSApp.setActivationPolicy(.accessory)

    Task { await notificationService.requestPermission() }

    let env = ProcessInfo.processInfo.environment
    let model = env["OPENROUTER_MODEL"] ?? "anthropic/claude-haiku-4-5"
    let aiService: AIService = OpenRouterAIService(
        apiKey: "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",
        model: model
    )

    let captureService = ScreenCaptureService()
    let ocrService = OCRService()

    Task { @MainActor in
        self.engine = AccountabilityEngine(
            state: self.state,
            captureService: captureService,
            ocrService: ocrService,
            aiService: aiService,
            notificationService: self.notificationService
        )
    }

    // Watch isCapturing
    Task { @MainActor in
        var last = false
        while true {
            let capturing = self.state.isCapturing
            if capturing != last {
                last = capturing
                capturing ? self.engine?.start() : self.engine?.stop()
            }
            try? await Task.sleep(for: .seconds(1))
        }
    }

    // Watch phase changes to resize panel
    Task { @MainActor in
        var lastPhase: AppPhase = .idle
        while true {
            let phase = self.state.appPhase
            if phase != lastPhase {
                lastPhase = phase
                self.panel?.resize(for: phase)
            }
            try? await Task.sleep(for: .milliseconds(200))
        }
    }

    Task { _ = await ScreenCaptureService().requestPermission() }

    guard let eng = engine else { return }
    let panel = FloatingPanel()
    panel.title = "AccountaBall"
    panel.contentView = NSHostingView(
        rootView: RootCoordinatorView(engine: eng, aiService: aiService)
            .environmentObject(state)
    )
    panel.resize(for: .welcome)
    panel.center()
    panel.orderFront(nil)
    self.panel = panel
}
```

**Note:** `engine` is initialized in a `Task { @MainActor in }` block asynchronously, which means it won't be ready when `panel.contentView` is set. Fix by making engine non-optional and initializing synchronously:

In `AppDelegate`, change:
```swift
var engine: AccountabilityEngine?
```
to initialize it directly before the panel (the `AccountabilityEngine.init` is not async):

```swift
let eng = AccountabilityEngine(
    state: state,
    captureService: captureService,
    ocrService: ocrService,
    aiService: aiService,
    notificationService: notificationService
)
self.engine = eng
```

Remove the `Task { @MainActor in self.engine = ... }` block.

**Step 4: Run tests — ensure all 37+ pass**
```bash
cd /Users/jericodelacruz/Desktop/AccountaBall/src
make test
```

**Step 5: Run the app**
```bash
make run
```

Verify:
- Ball bounces on launch
- Welcome screen appears with speech bubble
- "Let's get started" shows task setup table
- Rows unlock sequentially
- "Let's go!" starts session, ball moves to edge
- Timer counts up
- Clicking ball shows "What's up?" overlay

**Step 6: Commit**
```bash
git add Sources/AccountaBall/Views/RootCoordinatorView.swift Sources/AccountaBall/Views/FloatingPanel.swift Sources/AccountaBall/AppDelegate.swift
git commit -m "feat: wire RootCoordinatorView — full v2 app flow connected"
```

---

## Done Criteria

- [ ] Bounce animation plays on launch
- [ ] Welcome screen shows speech bubble + "Let's get started"
- [ ] Task setup: rows unlock sequentially, both columns required, autosaves
- [ ] "Let's go!" collapses to edge basketball with session + task timers
- [ ] 2× off-task: ball creeps in angry, asks "what are you doing?", AI judges excuse
- [ ] Clicking ball: "What's up?" → progress panel or dismiss
- [ ] Progress panel: per-task time, checkboxes, checking all triggers completion
- [ ] 3-point arc shot animation with hoop + confetti
- [ ] Session summary with restart / quit
- [ ] All unit tests still pass (`make test`)
