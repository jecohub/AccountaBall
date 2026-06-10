# AccountaBall Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a macOS floating ball widget that watches your screen every ~5s and holds you accountable to a declared task via Claude AI classification.

**Architecture:** NSPanel hosts a SwiftUI BallView that reacts to a 3-state machine (onTask / offTask / done). A background loop captures the screen via ScreenCaptureKit, extracts text via Vision OCR, then sends task + text to Claude API to classify state. An AIService protocol keeps Claude and Ollama swappable with zero engine changes.

**Tech Stack:** Swift 5.9+, macOS 14+ (Sonoma — required for `@Observable` + `SCScreenshotManager`), SwiftUI, AppKit/NSPanel, ScreenCaptureKit, Vision.framework, URLSession (Claude API)

---

## Phase 1: Foundation — Models & State Machine

### Task 1: Xcode Project Setup

**Files:**
- Create: Xcode project at `src/AccountaBall.xcodeproj`

**Step 1: Create the Xcode project**

Open Xcode → File → New → Project → macOS → App.
- Product Name: `AccountaBall`
- Team: your Apple ID
- Bundle Identifier: `com.yourname.AccountaBall`
- Language: Swift
- Interface: SwiftUI
- Uncheck "Include Tests" for now (we add them manually)
- Save to: `/Users/jericodelacruz/Desktop/AccountaBall/src/`

**Step 2: Set deployment target**

In Xcode: select the project in the navigator → AccountaBall target → General → Minimum Deployments → **macOS 14.0**

**Step 3: Add entitlements**

In Xcode: File → New → File → macOS → Property List → name it `AccountaBall.entitlements`.
Add these keys:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>com.apple.security.app-sandbox</key>
    <true/>
    <key>com.apple.security.network.client</key>
    <true/>
</dict>
</plist>
```

In target → Build Settings → search "Code Signing Entitlements" → set to `AccountaBall/AccountaBall.entitlements`

**Step 4: Add Info.plist keys for privacy strings**

In `Info.plist`, add:
```xml
<key>NSScreenCaptureUsageDescription</key>
<string>AccountaBall reads your screen to check if you're on task.</string>
```

**Step 5: Add XCTest target**

File → New → Target → macOS → Unit Testing Bundle → name: `AccountaBallTests`. Make sure "Target to be Tested" = AccountaBall.

**Step 6: Create folder groups in Xcode navigator**

Right-click AccountaBall group → New Group (without folder). Create:
- `Models`
- `Views`
- `Services`
- `Engine`

**Step 7: Git init**

```bash
cd /Users/jericodelacruz/Desktop/AccountaBall/src
git init
echo ".DS_Store\n*.xcuserstate\nxcuserdata/\nDerivedData/" > .gitignore
git add -A
git commit -m "chore: initial Xcode project setup"
```

---

### Task 2: BallState Enum

**Files:**
- Create: `AccountaBall/Models/BallState.swift`
- Create: `AccountaBallTests/BallStateTests.swift`

**Step 1: Write the failing test**

Create `AccountaBallTests/BallStateTests.swift`:

```swift
import XCTest
@testable import AccountaBall

final class BallStateTests: XCTestCase {
    func testEquality() {
        XCTAssertEqual(BallState.onTask, BallState.onTask)
        XCTAssertNotEqual(BallState.onTask, BallState.offTask)
    }

    func testAllCasesExist() {
        let states: [BallState] = [.idle, .onTask, .offTask, .done]
        XCTAssertEqual(states.count, 4)
    }
}
```

**Step 2: Run test — expect FAIL**

In Xcode: Cmd+U. Expected: compile error "cannot find type 'BallState'".

**Step 3: Implement BallState**

Create `AccountaBall/Models/BallState.swift`:

```swift
enum BallState: Equatable {
    case idle
    case onTask
    case offTask
    case done
}
```

**Step 4: Run tests — expect PASS**

Cmd+U. Both tests should pass.

**Step 5: Commit**

```bash
git add AccountaBall/Models/BallState.swift AccountaBallTests/BallStateTests.swift
git commit -m "feat: add BallState enum"
```

---

### Task 3: TaskSession Model

**Files:**
- Create: `AccountaBall/Models/TaskSession.swift`
- Create: `AccountaBallTests/TaskSessionTests.swift`

**Step 1: Write the failing test**

```swift
import XCTest
@testable import AccountaBall

final class TaskSessionTests: XCTestCase {
    func testDurationCalculation() {
        let start = Date()
        let end = start.addingTimeInterval(300)
        let session = TaskSession(task: "write proposal", startedAt: start, completedAt: end)
        XCTAssertEqual(session.duration, 300, accuracy: 0.001)
    }

    func testCodableRoundtrip() throws {
        let start = Date(timeIntervalSince1970: 1000)
        let end = Date(timeIntervalSince1970: 1300)
        let session = TaskSession(task: "write tests", startedAt: start, completedAt: end)
        let data = try JSONEncoder().encode(session)
        let decoded = try JSONDecoder().decode(TaskSession.self, from: data)
        XCTAssertEqual(decoded.task, "write tests")
        XCTAssertEqual(decoded.duration, 300, accuracy: 0.001)
    }
}
```

**Step 2: Run test — expect FAIL**

Cmd+U. Expected: compile error "cannot find type 'TaskSession'".

**Step 3: Implement TaskSession**

```swift
struct TaskSession: Codable, Equatable {
    let task: String
    let startedAt: Date
    let completedAt: Date

    var duration: TimeInterval {
        completedAt.timeIntervalSince(startedAt)
    }
}
```

**Step 4: Run tests — expect PASS**

**Step 5: Commit**

```bash
git add AccountaBall/Models/TaskSession.swift AccountaBallTests/TaskSessionTests.swift
git commit -m "feat: add TaskSession model"
```

---

### Task 4: AppState @Observable

**Files:**
- Create: `AccountaBall/Models/AppState.swift`
- Create: `AccountaBallTests/AppStateTests.swift`

**Step 1: Write the failing tests**

```swift
import XCTest
@testable import AccountaBall

final class AppStateTests: XCTestCase {
    func testInitialState() {
        let state = AppState()
        XCTAssertEqual(state.currentTask, "")
        XCTAssertEqual(state.ballState, .idle)
        XCTAssertFalse(state.isCapturing)
        XCTAssertTrue(state.sessionLog.isEmpty)
    }

    func testSubmitTaskSetsOnTask() {
        let state = AppState()
        state.submitTask("write the proposal")
        XCTAssertEqual(state.currentTask, "write the proposal")
        XCTAssertEqual(state.ballState, .onTask)
        XCTAssertTrue(state.isCapturing)
    }

    func testSubmitEmptyTaskIsIgnored() {
        let state = AppState()
        state.submitTask("   ")
        XCTAssertEqual(state.ballState, .idle)
        XCTAssertFalse(state.isCapturing)
    }

    func testClearTaskResetsToIdle() {
        let state = AppState()
        state.submitTask("write the proposal")
        state.clearTask()
        XCTAssertEqual(state.currentTask, "")
        XCTAssertEqual(state.ballState, .idle)
        XCTAssertFalse(state.isCapturing)
    }

    func testCompleteTaskAppendsToLog() {
        let state = AppState()
        state.submitTask("write the proposal")
        state.completeTask()
        XCTAssertEqual(state.sessionLog.count, 1)
        XCTAssertEqual(state.sessionLog.first?.task, "write the proposal")
        XCTAssertEqual(state.ballState, .idle)
        XCTAssertFalse(state.isCapturing)
    }
}
```

**Step 2: Run test — expect FAIL**

**Step 3: Implement AppState**

```swift
import Foundation
import Observation

@Observable
class AppState {
    var currentTask: String = ""
    var ballState: BallState = .idle
    var isCapturing: Bool = false
    var sessionLog: [TaskSession] = []

    private var taskStartedAt: Date?

    func submitTask(_ task: String) {
        let trimmed = task.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return }
        currentTask = trimmed
        ballState = .onTask
        isCapturing = true
        taskStartedAt = Date()
    }

    func clearTask() {
        currentTask = ""
        ballState = .idle
        isCapturing = false
        taskStartedAt = nil
    }

    func completeTask() {
        guard let startedAt = taskStartedAt else { return }
        let session = TaskSession(
            task: currentTask,
            startedAt: startedAt,
            completedAt: Date()
        )
        sessionLog.append(session)
        clearTask()
    }
}
```

**Step 4: Run tests — expect PASS**

**Step 5: Commit**

```bash
git add AccountaBall/Models/AppState.swift AccountaBallTests/AppStateTests.swift
git commit -m "feat: add AppState observable model"
```

---

### Task 5: AccountabilityEngine State Machine

**Files:**
- Create: `AccountaBall/Engine/AccountabilityEngine.swift`
- Create: `AccountaBallTests/AccountabilityEngineTests.swift`

**Step 1: Write the failing tests**

```swift
import XCTest
@testable import AccountaBall

final class AccountabilityEngineTests: XCTestCase {
    var state: AppState!
    var engine: AccountabilityEngine!

    override func setUp() {
        state = AppState()
        state.submitTask("write the proposal")
        engine = AccountabilityEngine(state: state)
    }

    func testSingleOffTaskDoesNotFlipState() {
        engine.processAIResult(.offTask)
        XCTAssertEqual(state.ballState, .onTask)
    }

    func testTwoConsecutiveOffTaskFlipsToOffTask() {
        engine.processAIResult(.offTask)
        engine.processAIResult(.offTask)
        XCTAssertEqual(state.ballState, .offTask)
    }

    func testOffTaskThenOnTaskResetsSuspicion() {
        engine.processAIResult(.offTask)
        engine.processAIResult(.onTask)
        engine.processAIResult(.offTask)
        // only 1 consecutive off-task after reset — should NOT flip
        XCTAssertEqual(state.ballState, .onTask)
    }

    func testDoneFlipsImmediately() {
        engine.processAIResult(.done)
        XCTAssertEqual(state.ballState, .done)
    }

    func testOnTaskWhileOffTaskReturnsToOnTask() {
        engine.processAIResult(.offTask)
        engine.processAIResult(.offTask)
        XCTAssertEqual(state.ballState, .offTask)
        engine.processAIResult(.onTask)
        XCTAssertEqual(state.ballState, .onTask)
    }
}
```

**Step 2: Run test — expect FAIL**

**Step 3: Implement AccountabilityEngine**

```swift
import Foundation

@MainActor
class AccountabilityEngine {
    private let state: AppState
    private var suspicionCount: Int = 0

    init(state: AppState) {
        self.state = state
    }

    func processAIResult(_ result: BallState) {
        switch result {
        case .onTask:
            suspicionCount = 0
            if state.ballState == .offTask {
                state.ballState = .onTask
            }
        case .offTask:
            suspicionCount += 1
            if suspicionCount >= 2 {
                state.ballState = .offTask
            }
        case .done:
            state.completeTask()
        case .idle:
            break
        }
    }
}
```

**Step 4: Run tests — expect PASS**

Note: XCTest runs on the main actor by default, so `@MainActor` on the engine is fine in tests.

**Step 5: Commit**

```bash
git add AccountaBall/Engine/AccountabilityEngine.swift AccountaBallTests/AccountabilityEngineTests.swift
git commit -m "feat: add AccountabilityEngine state machine with 2-check rule"
```

---

## Phase 2: UI Layer

### Task 6: Arc Shape + BallView

**Files:**
- Create: `AccountaBall/Views/BallView.swift`

No unit test for pure SwiftUI drawing — use the Xcode Preview.

**Step 1: Create BallView.swift**

```swift
import SwiftUI

struct Arc: Shape {
    let startAngle: Angle
    let endAngle: Angle
    let clockwise: Bool

    func path(in rect: CGRect) -> Path {
        var path = Path()
        path.addArc(
            center: CGPoint(x: rect.midX, y: rect.midY),
            radius: rect.width / 2,
            startAngle: startAngle,
            endAngle: endAngle,
            clockwise: clockwise
        )
        return path
    }
}

struct BallView: View {
    let state: BallState
    @State private var isPulsing = false

    var body: some View {
        ZStack {
            Circle()
                .fill(ballColor)
                .frame(width: 60, height: 60)
                .shadow(color: .black.opacity(0.25), radius: 4, y: 2)

            // Eyes
            HStack(spacing: eyeSpacing) {
                Circle().fill(.white).frame(width: eyeSize, height: eyeSize)
                Circle().fill(.white).frame(width: eyeSize, height: eyeSize)
            }
            .offset(y: -10)

            // Mouth
            mouthShape
                .stroke(.white, lineWidth: state == .done ? 3 : 2)
                .frame(width: mouthWidth, height: mouthHeight)
                .offset(y: 12)
        }
        .scaleEffect(isPulsing ? 1.08 : 1.0)
        .onChange(of: state) { _, newState in
            withAnimation(
                newState == .offTask
                    ? .easeInOut(duration: 0.6).repeatForever(autoreverses: true)
                    : .default
            ) {
                isPulsing = newState == .offTask
            }
        }
    }

    private var ballColor: Color {
        switch state {
        case .idle:    return .gray.opacity(0.6)
        case .onTask:  return .green
        case .offTask: return .orange
        case .done:    return .yellow
        }
    }

    private var eyeSize: CGFloat { state == .offTask ? 9 : 7 }
    private var eyeSpacing: CGFloat { 12 }
    private var mouthWidth: CGFloat { state == .done ? 28 : 20 }
    private var mouthHeight: CGFloat { state == .done ? 14 : 10 }

    @ViewBuilder
    private var mouthShape: some View {
        switch state {
        case .idle, .onTask:
            Arc(startAngle: .degrees(0), endAngle: .degrees(180), clockwise: false)
        case .offTask:
            Arc(startAngle: .degrees(0), endAngle: .degrees(180), clockwise: true)
        case .done:
            Arc(startAngle: .degrees(0), endAngle: .degrees(180), clockwise: false)
        }
    }
}

#Preview {
    HStack(spacing: 20) {
        BallView(state: .idle)
        BallView(state: .onTask)
        BallView(state: .offTask)
        BallView(state: .done)
    }
    .padding(20)
    .background(.black)
}
```

**Step 2: Verify preview in Xcode**

Open the file → click Resume in the preview canvas. You should see 4 ball states side-by-side on a black background. Verify each expression looks correct.

**Step 3: Commit**

```bash
git add AccountaBall/Views/BallView.swift
git commit -m "feat: add BallView with idle/onTask/offTask/done expressions"
```

---

### Task 7: TaskInputView

**Files:**
- Create: `AccountaBall/Views/TaskInputView.swift`

**Step 1: Create TaskInputView.swift**

```swift
import SwiftUI

struct TaskInputView: View {
    @Environment(AppState.self) var state
    @State private var inputText: String = ""
    @FocusState private var isFocused: Bool

    var body: some View {
        @Bindable var state = state
        HStack {
            TextField("What are you working on?", text: $inputText)
                .textFieldStyle(.plain)
                .font(.system(size: 12))
                .foregroundStyle(.white)
                .focused($isFocused)
                .onSubmit { submit() }

            if !inputText.isEmpty {
                Button(action: submit) {
                    Image(systemName: "arrow.right.circle.fill")
                        .foregroundStyle(.white.opacity(0.8))
                }
                .buttonStyle(.plain)
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .background(.white.opacity(0.15))
        .clipShape(RoundedRectangle(cornerRadius: 8))
        .frame(width: 220)
        .onAppear { isFocused = true }
    }

    private func submit() {
        state.submitTask(inputText)
        inputText = ""
    }
}
```

**Step 2: Commit**

```bash
git add AccountaBall/Views/TaskInputView.swift
git commit -m "feat: add TaskInputView"
```

---

### Task 8: FloatingPanel + ContentView + AppDelegate

**Files:**
- Create: `AccountaBall/Views/FloatingPanel.swift`
- Create: `AccountaBall/Views/ContentView.swift`
- Modify: `AccountaBall/AccountaBallApp.swift`

**Step 1: Create FloatingPanel.swift**

```swift
import AppKit
import SwiftUI

class FloatingPanel: NSPanel {
    init(contentRect: NSRect = NSRect(x: 0, y: 0, width: 96, height: 96)) {
        super.init(
            contentRect: contentRect,
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        level = .floating
        collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary]
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        isMovableByWindowBackground = true
        hidesOnDeactivate = false
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }

    func positionNearTopRight() {
        guard let screen = NSScreen.main else { return }
        let x = screen.visibleFrame.maxX - frame.width - 20
        let y = screen.visibleFrame.maxY - frame.height - 20
        setFrameOrigin(NSPoint(x: x, y: y))
    }
}
```

**Step 2: Create ContentView.swift**

```swift
import SwiftUI

struct ContentView: View {
    @Environment(AppState.self) var state
    @State private var showInput = false

    var body: some View {
        VStack(spacing: 6) {
            BallView(state: state.ballState)
                .onTapGesture {
                    if state.ballState == .idle || state.ballState == .onTask {
                        withAnimation(.spring(duration: 0.3)) {
                            showInput.toggle()
                        }
                    }
                }

            if showInput || state.ballState == .idle {
                TaskInputView()
                    .transition(.move(edge: .bottom).combined(with: .opacity))
            }
        }
        .padding(8)
        .onChange(of: state.ballState) { _, newState in
            if newState == .onTask {
                withAnimation { showInput = false }
            }
        }
    }
}

#Preview {
    let state = AppState()
    ContentView()
        .environment(state)
        .background(.black.opacity(0.01))
}
```

**Step 3: Replace AccountaBallApp.swift**

```swift
import SwiftUI
import AppKit

@main
struct AccountaBallApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var delegate

    var body: some Scene {
        Settings { EmptyView() }
    }
}

class AppDelegate: NSObject, NSApplicationDelegate {
    var panel: FloatingPanel?
    let state = AppState()

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory) // no Dock icon

        let panel = FloatingPanel()
        panel.contentView = NSHostingView(
            rootView: ContentView().environment(state)
        )
        panel.positionNearTopRight()
        panel.orderFront(nil)
        self.panel = panel
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ app: NSApplication) -> Bool {
        false
    }
}
```

**Step 4: Run the app — manual verify**

Cmd+R. Expected:
- No Dock icon appears (accessory app)
- A floating ball appears near the top-right corner
- Tapping it reveals the task text field
- Typing a task and pressing Enter flips the ball to green (onTask)
- The text field hides after submitting

**Step 5: Commit**

```bash
git add AccountaBall/Views/FloatingPanel.swift AccountaBall/Views/ContentView.swift AccountaBall/AccountaBallApp.swift
git commit -m "feat: floating NSPanel with SwiftUI ball and task input"
```

---

## Phase 3: Capture + OCR

### Task 9: OCRService

**Files:**
- Create: `AccountaBall/Services/OCRService.swift`
- Create: `AccountaBallTests/OCRServiceTests.swift`

**Step 1: Write the failing test**

```swift
import XCTest
@testable import AccountaBall

final class OCRServiceTests: XCTestCase {
    let service = OCRService()

    func testFiltersStringsUnder3Characters() {
        let result = service.filterText(["a", "is", "Hello", "World", "OK", "do"])
        XCTAssertEqual(result, ["Hello", "World"])
    }

    func testEmptyInputReturnsEmpty() {
        let result = service.filterText([])
        XCTAssertTrue(result.isEmpty)
    }

    func testJoinsRemainingWithNewlines() {
        let result = service.joinedText(["Hello", "World"])
        XCTAssertEqual(result, "Hello\nWorld")
    }
}
```

**Step 2: Run test — expect FAIL**

**Step 3: Implement OCRService**

```swift
import Foundation
import Vision
import AppKit

class OCRService {
    func extractText(from image: CGImage) async -> String {
        return await withCheckedContinuation { continuation in
            let request = VNRecognizeTextRequest { request, error in
                guard error == nil,
                      let observations = request.results as? [VNRecognizedTextObservation]
                else {
                    continuation.resume(returning: "")
                    return
                }
                let strings = observations.compactMap {
                    $0.topCandidates(1).first?.string
                }
                let filtered = self.filterText(strings)
                continuation.resume(returning: self.joinedText(filtered))
            }
            request.recognitionLevel = .accurate
            request.usesLanguageCorrection = true

            let handler = VNImageRequestHandler(cgImage: image)
            try? handler.perform([request])
        }
    }

    func filterText(_ strings: [String]) -> [String] {
        strings.filter { $0.count >= 3 }
    }

    func joinedText(_ strings: [String]) -> String {
        strings.joined(separator: "\n")
    }
}
```

**Step 4: Run tests — expect PASS**

**Step 5: Commit**

```bash
git add AccountaBall/Services/OCRService.swift AccountaBallTests/OCRServiceTests.swift
git commit -m "feat: add OCRService with Vision.framework text extraction"
```

---

### Task 10: ScreenCaptureService

**Files:**
- Create: `AccountaBall/Services/ScreenCaptureService.swift`

No unit test — ScreenCaptureKit requires a real screen and permissions. Manual test only.

**Step 1: Create ScreenCaptureService.swift**

```swift
import Foundation
import ScreenCaptureKit
import AppKit

class ScreenCaptureService {
    private var isCapturing = false

    func requestPermission() async -> Bool {
        do {
            _ = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
            return true
        } catch {
            return false
        }
    }

    func captureScreen(excluding panelTitle: String? = nil) async throws -> CGImage? {
        let content = try await SCShareableContent.excludingDesktopWindows(
            false,
            onScreenWindowsOnly: true
        )

        guard let display = content.displays.first else { return nil }

        let excludedWindows = panelTitle.map { title in
            content.windows.filter { $0.title == title }
        } ?? []

        let filter = SCContentFilter(display: display, excludingWindows: excludedWindows)

        let config = SCStreamConfiguration()
        config.width = Int(display.width)
        config.height = Int(display.height)
        config.captureResolution = .nominal

        return try await SCScreenshotManager.captureImage(
            contentFilter: filter,
            configuration: config
        )
    }

    func startLoop(
        interval: TimeInterval = 5,
        panelTitle: String? = nil,
        onCapture: @escaping (CGImage) async -> Void
    ) -> Task<Void, Never> {
        Task {
            while !Task.isCancelled {
                if let image = try? await captureScreen(excluding: panelTitle) {
                    await onCapture(image)
                }
                try? await Task.sleep(for: .seconds(interval))
            }
        }
    }
}
```

**Step 2: Wire permission request into AppDelegate**

In `AppDelegate.applicationDidFinishLaunching`, add after the panel setup:

```swift
Task {
    let captureService = ScreenCaptureService()
    let granted = await captureService.requestPermission()
    if !granted {
        // System will show the permission prompt automatically on first call
        print("Screen Recording permission not yet granted — user will be prompted")
    }
}
```

**Step 3: Manual test**

Run the app. On first launch, macOS will prompt for Screen Recording permission. Grant it in System Settings. Check the Xcode console — no crash means the service initialized correctly.

**Step 4: Commit**

```bash
git add AccountaBall/Services/ScreenCaptureService.swift AccountaBall/AccountaBallApp.swift
git commit -m "feat: add ScreenCaptureService with SCScreenshotManager loop"
```

---

## Phase 4: AI + Full Loop

### Task 11: AIService Protocol + ClaudeAIService

**Files:**
- Create: `AccountaBall/Services/AIService.swift`
- Create: `AccountaBall/Services/ClaudeAIService.swift`
- Create: `AccountaBallTests/ClaudeAIServiceTests.swift`

**Step 1: Write the failing tests (response parser only — no network)**

```swift
import XCTest
@testable import AccountaBall

final class ClaudeAIServiceTests: XCTestCase {
    func testParsesONTASK() {
        XCTAssertEqual(ClaudeAIService.parseResponse("ONTASK"), .onTask)
    }

    func testParsesOFFTASK() {
        XCTAssertEqual(ClaudeAIService.parseResponse("OFFTASK"), .offTask)
    }

    func testParsesDONE() {
        XCTAssertEqual(ClaudeAIService.parseResponse("DONE"), .done)
    }

    func testTrimsWhitespace() {
        XCTAssertEqual(ClaudeAIService.parseResponse("  ONTASK\n"), .onTask)
    }

    func testUnknownResponseDefaultsToOnTask() {
        XCTAssertEqual(ClaudeAIService.parseResponse("I'm not sure"), .onTask)
        XCTAssertEqual(ClaudeAIService.parseResponse(""), .onTask)
    }
}
```

**Step 2: Run test — expect FAIL**

**Step 3: Create AIService.swift**

```swift
protocol AIService {
    func classify(task: String, screenText: String) async throws -> BallState
}
```

**Step 4: Create ClaudeAIService.swift**

```swift
import Foundation

class ClaudeAIService: AIService {
    private let apiKey: String
    private let model = "claude-haiku-4-5-20251001"
    private let endpoint = URL(string: "https://api.anthropic.com/v1/messages")!

    private let systemPrompt = """
    You are an accountability assistant. The user declared a task.
    Look at what's on their screen and respond with exactly one word:
    ONTASK, OFFTASK, or DONE.

    Rules:
    - ONTASK: screen content is clearly related to the declared task
    - OFFTASK: screen shows something unrelated (social media, YouTube, unrelated apps)
    - DONE: the task appears completed (document finished, code committed, etc.)
    - When uncertain, respond ONTASK (benefit of the doubt)
    - Respond with ONLY the single word. No punctuation. No explanation.
    """

    init(apiKey: String) {
        self.apiKey = apiKey
    }

    func classify(task: String, screenText: String) async throws -> BallState {
        let userMessage = "Task: \(task)\n\nScreen text:\n\(screenText)"

        let body: [String: Any] = [
            "model": model,
            "max_tokens": 10,
            "system": systemPrompt,
            "messages": [["role": "user", "content": userMessage]]
        ]

        var request = URLRequest(url: endpoint)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue(apiKey, forHTTPHeaderField: "x-api-key")
        request.setValue("2023-06-01", forHTTPHeaderField: "anthropic-version")
        request.httpBody = try JSONSerialization.data(withJSONObject: body)

        let (data, _) = try await URLSession.shared.data(for: request)

        guard
            let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
            let content = (json["content"] as? [[String: Any]])?.first,
            let text = content["text"] as? String
        else { return .onTask }

        return Self.parseResponse(text)
    }

    static func parseResponse(_ raw: String) -> BallState {
        let cleaned = raw.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
        switch cleaned {
        case "ONTASK":  return .onTask
        case "OFFTASK": return .offTask
        case "DONE":    return .done
        default:        return .onTask
        }
    }
}
```

**Step 5: Run tests — expect PASS**

**Step 6: Commit**

```bash
git add AccountaBall/Services/AIService.swift AccountaBall/Services/ClaudeAIService.swift AccountaBallTests/ClaudeAIServiceTests.swift
git commit -m "feat: add AIService protocol and ClaudeAIService with response parser"
```

---

### Task 12: Wire the Full Accountability Loop

**Files:**
- Modify: `AccountaBall/Engine/AccountabilityEngine.swift`
- Modify: `AccountaBall/AccountaBallApp.swift`

**Step 1: Extend AccountabilityEngine to own the loop**

Replace the body of `AccountabilityEngine.swift`:

```swift
import Foundation

@MainActor
class AccountabilityEngine {
    private let state: AppState
    private let captureService: ScreenCaptureService
    private let ocrService: OCRService
    private let aiService: AIService
    private var captureTask: Task<Void, Never>?
    private var suspicionCount: Int = 0

    init(state: AppState, captureService: ScreenCaptureService, ocrService: OCRService, aiService: AIService) {
        self.state = state
        self.captureService = captureService
        self.ocrService = ocrService
        self.aiService = aiService
    }

    func start() {
        captureTask = captureService.startLoop(
            interval: 5,
            panelTitle: "AccountaBall"
        ) { [weak self] image in
            guard let self else { return }
            let text = await self.ocrService.extractText(from: image)
            guard !text.isEmpty else { return }
            let result = (try? await self.aiService.classify(
                task: await self.state.currentTask,
                screenText: text
            )) ?? .onTask
            await self.processAIResult(result)
        }
    }

    func stop() {
        captureTask?.cancel()
        captureTask = nil
        suspicionCount = 0
    }

    func processAIResult(_ result: BallState) {
        switch result {
        case .onTask:
            suspicionCount = 0
            if state.ballState == .offTask {
                state.ballState = .onTask
            }
        case .offTask:
            suspicionCount += 1
            if suspicionCount >= 2 {
                state.ballState = .offTask
            }
        case .done:
            stop()
            state.completeTask()
        case .idle:
            break
        }
    }
}
```

**Step 2: Wire engine into AppDelegate**

In `AccountaBallApp.swift`, update `AppDelegate`:

```swift
class AppDelegate: NSObject, NSApplicationDelegate {
    var panel: FloatingPanel?
    let state = AppState()
    var engine: AccountabilityEngine?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)

        // Read API key from environment (set in Xcode scheme or ~/.zshrc)
        let apiKey = ProcessInfo.processInfo.environment["ANTHROPIC_API_KEY"] ?? ""

        let captureService = ScreenCaptureService()
        let ocrService = OCRService()
        let aiService = ClaudeAIService(apiKey: apiKey)
        engine = AccountabilityEngine(
            state: state,
            captureService: captureService,
            ocrService: ocrService,
            aiService: aiService
        )

        // Start engine when isCapturing flips true
        Task { @MainActor in
            // Observe state changes using withObservationTracking loop
            while true {
                let capturing = state.isCapturing
                if capturing {
                    engine?.start()
                } else {
                    engine?.stop()
                }
                // Wait for next change
                await Task.yield()
                // Simple poll — replace with withObservationTracking if needed
                try? await Task.sleep(for: .seconds(1))
            }
        }

        let panel = FloatingPanel()
        panel.title = "AccountaBall"
        panel.contentView = NSHostingView(
            rootView: ContentView().environment(state)
        )
        panel.positionNearTopRight()
        panel.orderFront(nil)
        self.panel = panel
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ app: NSApplication) -> Bool { false }
}
```

**Step 3: Set the API key in Xcode scheme**

Product → Scheme → Edit Scheme → Run → Arguments → Environment Variables → add:
- Name: `ANTHROPIC_API_KEY`
- Value: your key from console.anthropic.com

**Step 4: Run the full loop — manual verify**

1. Launch the app
2. Grant Screen Recording permission if prompted
3. Type a task (e.g. "write the proposal") and submit
4. Ball turns green
5. Open Twitter or YouTube
6. Wait ~10s (2 capture cycles)
7. Ball should turn amber and pulse

**Step 5: Run unit tests — make sure nothing broke**

Cmd+U. All tests pass.

**Step 6: Commit**

```bash
git add AccountaBall/Engine/AccountabilityEngine.swift AccountaBall/AccountaBallApp.swift
git commit -m "feat: wire full accountability loop — capture → OCR → AI → state"
```

---

### Task 13: Off-Task Notifications

**Files:**
- Create: `AccountaBall/Services/NotificationService.swift`
- Modify: `AccountaBall/Engine/AccountabilityEngine.swift`

**Step 1: Add Notifications entitlement**

In `Info.plist`, no change needed — `UserNotifications` only needs runtime permission.

**Step 2: Create NotificationService.swift**

```swift
import UserNotifications

class NotificationService {
    func requestPermission() async {
        _ = try? await UNUserNotificationCenter.current().requestAuthorization(
            options: [.alert, .sound]
        )
    }

    func sendOffTaskNudge(task: String) {
        let content = UNMutableNotificationContent()
        content.title = "Hey, get back on track!"
        content.body = "You said you'd \(task.prefix(60))…"
        content.sound = .default

        let request = UNNotificationRequest(
            identifier: "off-task-\(Date().timeIntervalSince1970)",
            content: content,
            trigger: nil
        )
        UNUserNotificationCenter.current().add(request)
    }
}
```

**Step 3: Use NotificationService in AppDelegate**

Add `let notificationService = NotificationService()` to `AppDelegate`. In `applicationDidFinishLaunching`, add:

```swift
Task { await notificationService.requestPermission() }
```

Pass `notificationService` to the engine, and call `sendOffTaskNudge` inside `processAIResult` when flipping to `.offTask`:

```swift
case .offTask:
    suspicionCount += 1
    if suspicionCount >= 2 {
        state.ballState = .offTask
        notificationService.sendOffTaskNudge(task: state.currentTask)
    }
```

**Step 4: Manual test**

Go off-task for ~10s. A system notification should appear.

**Step 5: Commit**

```bash
git add AccountaBall/Services/NotificationService.swift AccountaBall/Engine/AccountabilityEngine.swift AccountaBall/AccountaBallApp.swift
git commit -m "feat: send notification when user goes off task"
```

---

## Ollama Swap (Future — no code now)

When ready, create `OllamaAIService.swift` implementing `AIService`:
- Endpoint: `http://localhost:11434/api/generate`
- Same prompt, same `parseResponse` parser
- In AppDelegate: swap `ClaudeAIService(apiKey:)` for `OllamaAIService()`
- Zero other changes needed — the protocol absorbs the swap

---

## Done Criteria

- [ ] Ball floats above all windows with no Dock icon
- [ ] Tapping ball reveals task input; submitting turns ball green
- [ ] Every ~5s: screenshot captured, OCR extracted, Claude called
- [ ] 2 consecutive off-task reads → ball turns amber + pulses + notification sent
- [ ] On-task read → ball returns to green, suspicion reset
- [ ] Task detected as done → session logged, ball returns to idle
- [ ] All unit tests pass (Cmd+U)
