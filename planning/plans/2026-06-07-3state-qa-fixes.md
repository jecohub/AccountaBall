# 3-State QA Fixes Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix three issues found in v3.2 3-state manual QA — jumpy break countdown, over-eager drift tagging, and rarely-fired AMBIGUOUS.

**Architecture:** (1) Disable App Nap while capturing so the per-second UI timer fires reliably. (2) Replace the `suspicionCount >= 2` off-task gate with a same-screen dwell timer (≥15s on the *same* off-task label, reset when the screen changes). (3) Widen the shared AMBIGUOUS prompt so borderline work-shaped content asks "is this related?" instead of being judged off-task.

**Tech Stack:** Swift 5/SwiftUI/SwiftData (macOS 14+). Custom micro-test runner — `make test` (NOT XCTest). New suites = `func runXxxTests()` using `suite("…") { expect(cond, "msg") }`, registered in `Tests/TestRunner/main.swift`. Build/run: `make build` / `make run`. Ollama-only scope.

**Conventions for the engine tests:** `AccountabilityEngineTests.swift` already has a `Box` clock holder and injects `engine.now = { box.now }`. Advance simulated time by mutating `box.now`. Call `engine.resetSettleWindowToPast()` to bypass the settle window when you want a drift to be eligible.

---

### Task 1: Same-screen drift dwell timer

**Files:**
- Modify: `src/Sources/AccountaBall/Util/AppConstants.swift`
- Modify: `src/Sources/AccountaBall/Engine/AccountabilityEngine.swift` (the `.offTask` branch in `processResult`, ~514; add streak fields near `suspicionCount`; add `clearOffTaskStreak()`; call it everywhere `suspicionCount = 0`)
- Test: `src/Tests/AccountaBallTests/AccountabilityEngineTests.swift`

**Step 1: Add the constant**

```swift
/// A drift is only confirmed after the user stays on the SAME off-task screen
/// for this long. Switching off-task screens restarts the clock.
static let driftConfirmSeconds: TimeInterval = 15
```

**Step 2: Write the failing tests** (new `suite`s in `AccountabilityEngineTests.swift`)

- `same-screen <15s does not confirm`: two `.offTask("yt")` reads at the same `box.now` → `appPhase == .session`, `driftCount == 0`.
- `same-screen ≥15s confirms once`: `.offTask("yt")` at t0, advance `box.now` +16s, `.offTask("yt")` → `appPhase == .offTask`, `driftCount == 1`.
- `switching screens resets the clock`: `.offTask("yt")` at t0; +8s `.offTask("twitter")`; +8s `.offTask("twitter")` (16s total off-task, but only 8s on twitter) → still `.session`, `driftCount == 0`.
- `on-task between reads resets the streak`: `.offTask("yt")` t0; +10s `.onTask(0,"work")`; +10s `.offTask("yt")` → `.session` (streak restarted).

Call `engine.resetSettleWindowToPast()` after `beginSession` in each.

**Step 3: Run to verify they fail**

Run: `cd src && make test`
Expected: FAIL (drift still fires on 2 same-instant reads / never on the dwell path).

**Step 4: Implement**

Add fields near `suspicionCount`:

```swift
/// Start of the current uninterrupted off-task stay on ONE screen (nil = not
/// currently off-task). A drift confirms only once this exceeds
/// `driftConfirmSeconds`; switching off-task screens restarts it.
private var offTaskStreakStart: Date?
private var offTaskStreakActivity: String = ""

private func clearOffTaskStreak() {
    offTaskStreakStart = nil
    offTaskStreakActivity = ""
}
```

Replace the body of the `.offTask` branch's confirmation logic (keep the activity-grace block above it untouched):

```swift
suspicionCount += 1
state.activeTaskIndex = nil
// Same-screen dwell: keep the clock running while the off-task label is
// unchanged; restart it the moment the screen changes ("reset per screen").
if offTaskStreakStart == nil || !Self.activityMatches(label, offTaskStreakActivity) {
    offTaskStreakStart = now()
    offTaskStreakActivity = label
}
let dwell = now().timeIntervalSince(offTaskStreakStart!)
dbg("offTask (\"\(label)\") dwell=\(Int(dwell))s")
if dwell >= AppConstants.driftConfirmSeconds, !inSettleWindow, state.appPhase == .session {
    dbg("ENTER offTask phase (confirmed drift after \(Int(dwell))s)")
    logCheck(kind: "offtask", justified: false, activity: label,
             excuse: "(drifted)", rule: "off-task", taskIndex: nil)
    state.ballState = .offTask
    state.appPhase = .offTask
    notificationService.sendOffTaskNudge(task: state.activeTasks.first?.task ?? "")
}
```

Add `clearOffTaskStreak()` next to every `suspicionCount = 0` (in `.onTask`, `.ambiguous`, `resumeAfterExcuse`, `takeBreak`, `stop`, and after `completeTask`/`.done`). `beginSession` should also call it.

**Step 5: Run tests**

Run: `cd src && make test`
Expected: PASS. Update the older `AccountabilityEngineTests` cases that relied on "two consecutive offTask flips" to advance `box.now` +16s on the same label (and the `EngineSettleWindowTests` grace cases likewise).

**Step 6: Commit**

```bash
git add src/Sources/AccountaBall/Util/AppConstants.swift \
        src/Sources/AccountaBall/Engine/AccountabilityEngine.swift \
        src/Tests/AccountaBallTests/AccountabilityEngineTests.swift \
        src/Tests/AccountaBallTests/EngineSettleWindowTests.swift
git commit -m "fix: confirm drift only after 15s on the same off-task screen"
```

---

### Task 2: Widen AMBIGUOUS in the shared prompt

**Files:**
- Modify: `src/Sources/AccountaBall/Util/AIPrompts.swift` (`classifySystem`, ~52-67)

**Step 1: Rewrite the AMBIGUOUS / OFFTASK lines**

```
- AMBIGUOUS — work-shaped content whose connection to a task is not obvious:
  a document, spreadsheet, code editor, terminal, email, chat, an article, API
  docs, or an unfamiliar web page that could plausibly be research or prep for a
  task. When in doubt about anything productivity-like, choose AMBIGUOUS.
- OFFTASK — clearly leisure or personal, with no plausible work link: games,
  entertainment video, scrolling a social feed, shopping, sports/news for fun,
  messaging friends.
```

Keep the existing "When unsure, prefer TASK:N or AMBIGUOUS over OFFTASK" and `label` guidance.

**Step 2: Build**

Run: `cd src && make build`
Expected: compiles. (No unit test asserts prompt wording; this is an LLM-behavior change verified manually in Task 4.)

**Step 3: Commit**

```bash
git add src/Sources/AccountaBall/Util/AIPrompts.swift
git commit -m "feat: widen AMBIGUOUS so borderline work content asks instead of accusing"
```

---

### Task 3: Smooth break countdown (disable App Nap while capturing)

**Files:**
- Modify: `src/Sources/AccountaBall/Engine/AccountabilityEngine.swift` (`start()` / `stop()`)
- Test: `src/Tests/AccountaBallTests/AccountabilityEngineTests.swift`

**Step 1: Write the failing test**

```swift
// in a new suite
let (_, engine) = make()
expect(engine.isPreventingAppNap == false, "no activity token before start")
engine.start()
expect(engine.isPreventingAppNap == true, "start() holds an App Nap activity")
engine.stop()
expect(engine.isPreventingAppNap == false, "stop() releases it")
```

**Step 2: Run to verify it fails**

Run: `cd src && make test`
Expected: FAIL (`isPreventingAppNap` undefined).

**Step 3: Implement**

Add to the engine:

```swift
/// Held while capturing so macOS App Nap can't throttle the per-second UI
/// timers (the app runs as `.accessory` and is never frontmost, so a backgrounded
/// break countdown otherwise updates only every several seconds).
private var appNapToken: NSObjectProtocol?
var isPreventingAppNap: Bool { appNapToken != nil }
```

In `start()` (top): `if appNapToken == nil { appNapToken = ProcessInfo.processInfo.beginActivity(options: [.userInitiated], reason: "AccountaBall focus session") }`
In `stop()`: `if let t = appNapToken { ProcessInfo.processInfo.endActivity(t); appNapToken = nil }`

**Step 4: Run tests**

Run: `cd src && make test`
Expected: PASS.

**Step 5: Commit**

```bash
git add src/Sources/AccountaBall/Engine/AccountabilityEngine.swift \
        src/Tests/AccountaBallTests/AccountabilityEngineTests.swift
git commit -m "fix: keep app awake during capture so break countdown ticks per-second"
```

---

### Task 4: Manual verification

Run: `cd src && make run` (Ollama running, `qwen2.5:7b` pulled).

1. **Break countdown** — trigger a drift → "Take a timed 5-min break" → confirm the countdown decrements 5:00 → 4:59 → 4:58 smoothly while you work in another app.
2. **Drift dwell** — glance at an off-task screen briefly (<15s) and return → no drift logged. Stay off-task on one screen >15s → drift confirms once.
3. **Screen-switch** — hop between two off-task apps every few seconds → no drift until one screen is held >15s.
4. **AMBIGUOUS** — open a borderline doc/article/terminal not clearly tied to a task → the calm "is this related?" ask should now appear (once per screen). Test both "related" (allowance created, no re-ask) and "not related" (logs a drift).
5. **Recap transparency log** — confirm brief glances no longer show as "you drifted"; only sustained off-task stays do.

Then update `docs/HOW-IT-WORKS.md` if any user-facing behavior described there changed (drift timing, ambiguous criteria).

---

## Notes / out of scope
- Ollama only; OpenRouter path inherits the shared prompt change but is not QA'd here.
- Recap "feels like a boss" tone — explicitly deferred per user.
- `handleExcuse` remains dead-but-tested; not touched.
