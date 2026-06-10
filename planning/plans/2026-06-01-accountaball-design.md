# AccountaBall — Design Document
_2026-06-01_

## Overview

AccountaBall is a macOS floating widget that watches your screen every ~5 seconds and keeps you accountable to a task you declare. A smiling ball face floats above all windows, changing expression based on whether you're on-task, off-task, or done.

**macOS 13+ (Ventura) required.** Permissions needed: Screen Recording, Notifications.

---

## Architecture

4 layers, built in strict phase order so there's always something runnable:

```
UI Layer       →  FloatingPanel (NSPanel) + BallView (SwiftUI)
Capture Layer  →  ScreenCaptureService (ScreenCaptureKit, ~5s loop)
                  OCRService (Vision.framework, offline)
AI Brain       →  AIService protocol → ClaudeAIService / OllamaAIService
Output         →  Ball expression + NSUserNotification + confetti
```

---

## Section 1: Build Phases

| Phase | What | Goal |
|-------|------|------|
| 1 | Floating panel + ball UI | Something visible and satisfying on screen |
| 2 | ScreenCaptureKit + OCR | Real text extracted from screen |
| 3 | Claude API integration | Full accountability loop running |
| 4 | Ollama swap | Cheaper local inference |

---

## Section 2: UI Layer

### FloatingPanel.swift
NSPanel subclass configured as:
- `styleMask: [.borderless, .nonactivatingPanel]` — no title bar, never steals focus
- `level = .floating` — above all app windows
- `collectionBehavior = [.canJoinAllSpaces, .stationary]` — visible on every Space
- `isOpaque = false`, `backgroundColor = .clear` — transparent background
- `hasShadow = true` — subtle depth
- Hover show/hide via `NSTrackingArea` — 0.2s opacity fade

### BallView.swift
SwiftUI view driven by `BallState`:

| State | Color | Expression | Animation |
|-------|-------|-----------|-----------|
| idle | gray | neutral smile | none |
| onTask | green | gentle smile | none |
| offTask | amber | slight frown, wide eyes | repeating pulse (scaleEffect) |
| done | gold | big grin, squinting happy eyes | one-shot confetti burst |

Ball size: ~60pt. Expression built from SwiftUI primitives (`Circle`, dot eyes, curved `Path` for mouth).

### TaskInputView.swift
`TextField` that slides out from beneath the ball on click. Placeholder: *"What are you working on?"*. Submitting sets `AppState.currentTask` and starts the capture loop.

---

## Section 3: Data Model & State Machine

### AppState.swift
```swift
@Observable class AppState {
    var currentTask: String = ""
    var ballState: BallState = .idle
    var isCapturing: Bool = false
    var sessionLog: [TaskSession] = []
}
```

### TaskSession
```swift
struct TaskSession: Codable {
    let task: String
    let startedAt: Date
    let completedAt: Date
    var duration: TimeInterval { completedAt.timeIntervalSince(startedAt) }
}
```

Persisted to `~/Library/Application Support/AccountaBall/sessions.json`.

### State Machine

```
idle → onTask      user submits a task
onTask → offTask   AI returns offTask on 2 consecutive checks
offTask → onTask   AI returns onTask again
onTask → done      AI returns done on 1 check
any → idle         user clears the task
```

**2-check rule before `offTask`**: a single mis-read screenshot (loading spinner, brief context switch) doesn't trigger a false alarm. `suspicionCount` increments on each off-task read; two consecutive → state flips.

---

## Section 4: Capture + OCR Layer

### ScreenCaptureService.swift
- Uses `SCScreenshotManager.captureImage(for:configuration:)` — single-shot API (simpler than streaming `SCStream` for a periodic poll)
- Excludes AccountaBall's own panel from capture content
- Runs on a background `Task`, sleeps 5s between captures
- First launch triggers Screen Recording permission prompt automatically

```swift
func startCapturing() async {
    while isCapturing {
        let image = try await captureScreen()
        let text = await ocr.extractText(from: image)
        await engine.analyze(screenText: text)
        try await Task.sleep(for: .seconds(5))
    }
}
```

### OCRService.swift
- `VNRecognizeTextRequest` with `.accurate` recognition level
- Strips strings under 3 characters before sending to AI (removes noise)
- Entirely offline — no network call

---

## Section 5: AI Layer

### AIService Protocol
```swift
protocol AIService {
    func classify(task: String, screenText: String) async throws -> BallState
}
```

### ClaudeAIService.swift
- Model: `claude-haiku-4-5-20251001` — fast (~1s), cheap, ideal for 5s polling
- Endpoint: `api.anthropic.com/v1/messages`

**System prompt:**
```
You are an accountability assistant. The user declared a task.
Look at what's on their screen and respond with exactly one word:
ONTASK, OFFTASK, or DONE.

Rules:
- ONTASK: screen content is clearly related to the declared task
- OFFTASK: screen shows something unrelated (social media, YouTube, unrelated apps)
- DONE: the task appears completed (doc is finished, code is committed, etc.)
- When uncertain, respond ONTASK (benefit of the doubt)
- Respond with ONLY the single word. No punctuation. No explanation.
```

Response parsing: trim whitespace → match against three keywords. Unexpected response → `.onTask` (safe default).

### OllamaAIService.swift
- Endpoint: `http://localhost:11434/api/generate`
- Same protocol, same prompt, same parser
- Zero engine-layer changes when swapping from Claude

### AccountabilityEngine.swift
- Owns `suspicionCount`
- Calls `AIService.classify()`
- Applies 2-check rule
- Publishes state changes to `AppState` on `@MainActor`

---

## File Map

```
src/
  AccountaBallApp.swift       — app entry, NSApplicationDelegate
  Models/
    AppState.swift            — @Observable shared state
    BallState.swift           — enum + expression metadata
    TaskSession.swift         — Codable session log entry
  Views/
    FloatingPanel.swift       — NSPanel subclass
    BallView.swift            — SwiftUI smiling ball
    TaskInputView.swift       — task declaration text field
  Services/
    ScreenCaptureService.swift
    OCRService.swift
    AIService.swift           — protocol
    ClaudeAIService.swift
    OllamaAIService.swift
    SessionStore.swift        — JSON persistence
  Engine/
    AccountabilityEngine.swift — orchestrates capture → OCR → AI → state
```

---

## What's Out of Scope (for now)
- Voice input for task declaration
- Multi-task sessions
- Stats dashboard / history UI
- Streaks or gamification
- iCloud sync
