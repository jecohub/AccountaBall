# AccountaBall v2 — Design Document
_2026-06-02_

## Overview

A major redesign of AccountaBall adding rich animated flows, multi-task support, per-task time tracking, and a conversational off-task intervention system. Built entirely in Swift + SwiftUI — no Remotion or external animation libraries.

The existing core (ScreenCaptureKit, Vision OCR, AI classification engine) is preserved and extended to support multiple tasks with context.

---

## App States

```
IDLE → WELCOME → SETUP → SESSION → PROGRESS → COMPLETE
                             ↕
                         OFF_TASK
```

| State | Description |
|---|---|
| `IDLE` | App just launched, no saved tasks |
| `WELCOME` | Bounce animation + "Ready to finish your tasks?" screen |
| `SETUP` | 2-column task input table |
| `SESSION` | Basketball on screen edge with timers |
| `OFF_TASK` | Ball creeps in with angry face, asks "what are you doing?" |
| `PROGRESS` | Panel: elapsed time, per-task times, completion checkboxes |
| `COMPLETE` | 3-point arc shot animation + session summary |

**Return visit flow:** If tasks are saved from last session, app goes to `WELCOME` with tasks pre-filled — user just hits "Let's go!" to resume.

---

## Section 1: Welcome Screen + Bounce Animation

**Panel size:** ~400×300pt, centered on screen.

**Bounce animation** (`PhaseAnimator`, 3 phases):
- Phase 1: Ball drops fast from top-center → hits center (large impact, `scaleEffect(x:1.3, y:0.7)` squish)
- Phase 2: Bounces up 60% height → returns (medium impact)
- Phase 3: Bounces up 30% height → settles (small impact, brief squish)
- Each phase uses a spring with increasing damping — natural deceleration

**Welcome UI** (fades in after ball settles):
- Ball centered, smiling face
- Speech bubble above: *"Ready to finish your tasks?"*
- Button below: **"Let's get started"**
- If saved tasks exist: *"Welcome back — you have N tasks from last time"* + small "start fresh" link

"Let's get started" → transitions to `SETUP` (pre-filled if saved tasks exist).

---

## Section 2: Task Setup Screen

**Panel size:** ~500×400pt. Ball shrinks to small avatar in top-left corner.

**Table: 5 rows × 2 columns**

| Task | Context |
|---|---|
| What you need to do | Why / extra info to help the AI |

**Row unlock rules:**
- Rows 2–5 are dimmed until the previous row has both columns filled
- Each row unlocks the next only when both fields are non-empty
- Subtle `+` hint appears below the last filled row

**Validation:**
- "Let's go!" disabled until ≥ 1 complete row exists
- Attempting "Let's go!" with partial rows highlights empty fields with a soft red border

**Persistence:**
- Tasks + context autosaved to `UserDefaults` as JSON on every keystroke
- Restored and pre-filled on next launch

**"Let's go!" transition:**
- Table fades out, ball grows back to full size
- Ball bounces once, arcs to the right edge of the screen
- App enters `SESSION` state

---

## Section 3: Session Mode — Ball on the Edge

**Appearance:** Basketball (orange, black curved lines via SwiftUI `Path`). No face. Half visible on right screen edge, half off-screen.

**Timer display** (on the visible flat half):
```
  00:14:32   ← session timer (continuous since "Let's go!")
  ─────────
  Task 2     ← task the AI currently thinks you're on
  00:03:11   ← accumulated time on that task this session
```

**Timer mechanics:**
- Session timer: runs continuously
- Per-task timers: increment only when AI classifies user as actively on that task — fully automatic
- If AI classifies as off-task: no task timer increments

**Ball behavior:**
- Always on top, draggable vertically along the screen edge
- Subtly pulses every few seconds (scale 1.0 → 1.05 → 1.0)
- Clicking triggers the "What's up?" interaction

---

## Section 4: Off-Task Interaction

Triggered after **2 consecutive off-task AI reads** (existing rule).

**Creep animation:**
1. Ball rotates 180° on Y-axis — timer side rotates away, angry face revealed
2. Ball slides inward from edge slowly (~1.5s) — stops 1/3 into screen
3. Speech bubble: *"What are you doing?"*
4. Text input field appears for user's explanation

**AI evaluation of explanation:**
- Input: explanation text + all tasks + their context + current screen OCR
- **Justified** → ball smiles, *"Okay, carry on 👍"*, rotates back to timer, slides to edge
- **Not justified** → ball stays angry, *"Get back to work."*, slides to edge, keeps angry face for one full monitoring cycle

**Timeout:** If user ignores the prompt for 60 seconds, ball slides back automatically and resumes monitoring.

---

## Section 5: "What's Up?" + Progress View

**Triggered by:** clicking the ball in normal session mode.

Ball slides in smiling, speech bubble: *"What's up?"*

Two buttons:
- **"Show my progress"** → opens progress panel
- **"Nothing"** → ball slides back, resumes monitoring

**Progress panel:**
```
⏱ Session time      00:47:12
────────────────────────────────
  Task                    Time      Done?
  Write proposal          00:23:41   ☐
  Review slides           00:14:05   ☐
  Reply to emails         00:09:26   ☑  (grayed + strikethrough)
  Update roadmap          00:00:00   ☐
────────────────────────────────
              [ Back to work ]
```

- Checking a task removes it from AI monitoring immediately
- Time on task is read-only, accumulated automatically
- Checking the last remaining task triggers the completion animation instead of closing

---

## Section 6: Completion — 3-Point Shot

**Animation sequence:**
1. Basketball appears small on right side of screen
2. Launches on a parabolic arc leftward across full screen width (`keyframeAnimator`)
3. Rotates with backspin throughout the arc
4. Hoop appears on left side (orange rim circle + net drawn with `Path`)
5. Ball passes through hoop, net swishes downward
6. Confetti bursts from hoop (colored circles with physics-like spread)

**Session summary screen:**
```
        🏀  Session Complete!

   Total time        01:12:44
   Tasks finished    5 / 5

   [  Start a new session  ]
   [  Quit  ]
```

"Start a new session" → back to `WELCOME` with saved tasks pre-filled.

---

## Data Model Changes

### New: `TaskItem`
```swift
struct TaskItem: Codable, Identifiable {
    let id: UUID
    var task: String
    var context: String
    var isComplete: Bool
    var timeOnTask: TimeInterval  // accumulated by AI engine
}
```

### Updated: `AppState`
```swift
// Replaces single `currentTask: String`
var tasks: [TaskItem]           // up to 5
var activeTaskIndex: Int?       // AI-inferred current task
var sessionStartTime: Date?
var appPhase: AppPhase          // the 7 states above
```

### Persistence
- `tasks` saved to `UserDefaults` as JSON (autosave on edit)
- `timeOnTask` only persisted at session end (not mid-session)

---

## AI Engine Changes

### Classification prompt update
The prompt now includes all tasks + context, and asks the AI to return:
- Which task index (0–4) the user appears to be working on, OR
- `"OFFTASK"` if none match
- `"DONE"` if a specific task appears complete

### Explanation evaluation
Separate AI call when user submits an excuse:
- System prompt: accountability judge role
- Input: excuse text + active tasks + OCR text
- Output: `"JUSTIFIED"` or `"NOT_JUSTIFIED"`

---

## What Stays the Same

- `ScreenCaptureService` — unchanged
- `OCRService` — unchanged
- `OpenRouterAIService` / `ClaudeAIService` — extended with new prompts, same HTTP layer
- `NotificationService` — unchanged
- `Makefile` build system — unchanged
- 37 existing unit tests — all still pass
