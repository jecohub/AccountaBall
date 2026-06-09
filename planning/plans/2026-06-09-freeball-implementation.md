# FreeBall Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add FreeBall — a passive observation mode that silently captures full screen text every cycle (no mid-session AI, no nudging), then on End Session produces a narrative + categorized time breakdown + cross-session insight.

**Architecture:** A new `FreeBallEngine` (separate from `AccountabilityEngine`) owns its own capture loop, reusing `ScreenCaptureService` + `OCRService`. Each cycle it dedups OCR text against the last stored block and writes/extends a `FreeBallCapture`. No AI runs until `end()`, which assembles the deduped transcript + past session recaps and makes one `summarizeFreeBall` call. Three new `AppPhase` cases (`.freeBall`, `.freeBallLog`, `.freeBallRecap`) drive the UI. Data persists in two new SwiftData models, kept forever.

**Tech Stack:** Swift, SwiftUI, NSPanel, SwiftData (macOS 14+), Ollama/OpenRouter via the existing `AIService` protocol. Custom MicroTest harness (`expect(boolExpr, "desc")`). Build: `make -C src build`. Test: `make -C src test`.

**Design doc:** `planning/plans/2026-06-09-freeball-design.md`

**Conventions learned from the codebase (follow these):**
- Tests use `suite("Name") { expect(bool, "desc") }`. Each suite file defines `func runXTests()` and MUST be registered in `src/Tests/TestRunner/main.swift` → `runAllTests()` (sync suites inline; `@MainActor async` engine suites `await`ed at the bottom).
- All work commits in the inner `src/` repo (the outer repo gitignores `src/`). Run git commands from `src/`.
- Cycle cadence is `AppConstants.cycleSeconds` (3s), not 5s.
- Engine state mutations are `@MainActor`; OCR/AI awaits happen inside `@MainActor` methods (the awaits hop off main automatically).
- Adding an `AppPhase` case breaks three exhaustive switches (compiler-enforced): `FloatingPanel.resize(for:)`, `RootCoordinatorView.body`, and `AppPhaseTests`. Adding an `AIService` protocol method breaks ALL conformers (3 real + every test fake).

---

### Task 1: AppPhase scaffolding (3 new phases + exhaustive switches)

Add the phases first with placeholder UI so everything compiles; real views land in Tasks 10–12.

**Files:**
- Modify: `src/Sources/AccountaBall/Models/AppPhase.swift`
- Modify: `src/Sources/AccountaBall/Views/FloatingPanel.swift:31-66`
- Modify: `src/Sources/AccountaBall/Views/RootCoordinatorView.swift:10-64`
- Test: `src/Tests/AccountaBallTests/AppPhaseTests.swift`

**Step 1: Read the existing AppPhaseTests to mirror its exhaustiveness-guard style.**

Run: `sed -n '1,80p' src/Tests/AccountaBallTests/AppPhaseTests.swift`

**Step 2: Add a failing assertion** that the new phases exist. Append inside the existing suite (or add a new suite) in `AppPhaseTests.swift`:

```swift
suite("AppPhase_freeBall") {
    let phases: [AppPhase] = [.freeBall, .freeBallLog, .freeBallRecap]
    expect(phases.count == 3, "three FreeBall phases exist")
    // Exhaustiveness: this switch fails to compile if a case is unhandled.
    for p in phases {
        switch p {
        case .freeBall, .freeBallLog, .freeBallRecap: expect(true, "covered")
        default: expect(true, "other")
        }
    }
}
```

Register `runAppPhaseTests` is already in the runner — no change if you appended to the existing function. If you added a new `runX`, register it in `src/Tests/TestRunner/main.swift`.

**Step 3: Run test to verify it fails to compile**

Run: `make -C src test`
Expected: FAIL — `.freeBall` etc. are not members of `AppPhase`.

**Step 4: Add the cases.** In `AppPhase.swift`, after `case complete`:

```swift
    case complete
    case aiUnavailable
    // FreeBall — passive observation mode (separate engine).
    case freeBall       // collapsed calm "observing" ball
    case freeBallLog    // live session log: timer + End Session
    case freeBallRecap  // post-session summary + breakdown
```

**Step 5: Add resize cases** in `FloatingPanel.resize(for:)` before the closing brace of the switch:

```swift
        case .freeBall:
            // Small calm ball peeking at the edge, like .session.
            anchorRightEdge(size: NSSize(width: 130, height: 170))
        case .freeBallLog:
            // Compact centered card: timer + End Session.
            setContentSize(clampedToScreen(NSSize(width: 300, height: 260))); center()
        case .freeBallRecap:
            // Roomy centered recap card (narrative + bars + insight).
            setContentSize(clampedToScreen(NSSize(width: 520, height: 540))); center()
```

**Step 6: Add placeholder branches** in `RootCoordinatorView.body`'s switch (replaced with real views in Tasks 10–12):

```swift
            case .freeBall, .freeBallLog, .freeBallRecap:
                // Placeholder — real views land in Tasks 10–12.
                Color.black.ignoresSafeArea()
                    .transition(.opacity)
```

**Step 7: Run tests to verify they pass**

Run: `make -C src test`
Expected: PASS (all suites, including the new assertions).

**Step 8: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): add AppPhase cases + panel sizing scaffolding"
```

---

### Task 2: FreeBall data models + schema

**Files:**
- Create: `src/Sources/AccountaBall/Models/FreeBallModels.swift`
- Modify: `src/Sources/AccountaBall/Models/Persistence.swift:91-95` (Schema)
- Test: `src/Tests/AccountaBallTests/FreeBallPersistenceTests.swift`
- Modify: `src/Tests/TestRunner/main.swift`

**Step 1: Write the failing test** at `FreeBallPersistenceTests.swift` (mirror `PersistenceTests.swift`):

```swift
import SwiftData
@testable import AccountaBall

@MainActor
func runFreeBallPersistenceTests() {
    suite("FreeBallPersistence") {
        guard let container = try? AccountaBallStore.makeContainer(inMemory: true) else {
            expect(false, "container builds"); return
        }
        let ctx = container.mainContext
        let session = FreeBallSession(startedAt: .now)
        session.cycleCount = 12
        session.narrative = "Mostly coding."
        session.categories = [CategorySpan(label: "Coding", minutes: 45),
                              CategorySpan(label: "Email", minutes: 10)]
        session.insight = "You code in long blocks."
        let cap = FreeBallCapture(firstSeenAt: .now, lastSeenAt: .now, text: "hello world")
        session.captures.append(cap)
        ctx.insert(session)
        try? ctx.save()

        let fetched = (try? ctx.fetch(FetchDescriptor<FreeBallSession>())) ?? []
        expect(fetched.count == 1, "one FreeBallSession persisted")
        expect(fetched.first?.captures.count == 1, "capture related")
        expect(fetched.first?.categories.count == 2, "categories round-trip")
        expect(fetched.first?.categories.first?.label == "Coding", "category label round-trip")
        expect(fetched.first?.narrative == "Mostly coding.", "narrative round-trip")
    }
}
```

**Step 2: Register** in `src/Tests/TestRunner/main.swift`, in the sync block (after `runPersistenceTests()`):

```swift
    runFreeBallPersistenceTests()
```

**Step 3: Run to verify it fails**

Run: `make -C src test`
Expected: FAIL — `FreeBallSession` / `CategorySpan` / `FreeBallCapture` undefined.

**Step 4: Create the models** at `FreeBallModels.swift`:

```swift
import SwiftData
import Foundation

/// A labeled slice of session time (the categorized breakdown). Stored as a
/// Codable value on FreeBallSession and round-tripped to the recap UI.
struct CategorySpan: Codable, Equatable {
    var label: String
    var minutes: Int
}

@available(macOS 14, *)
@Model
final class FreeBallSession {
    var id: UUID = UUID()
    var startedAt: Date
    var endedAt: Date?
    var cycleCount: Int = 0
    // Distilled recap (kept long-term; feeds cross-session learning).
    var narrative: String = ""
    var categories: [CategorySpan] = []
    var insight: String = ""
    /// True when End Session couldn't reach the AI; raw captures kept for a later pass.
    var recapPending: Bool = false
    @Relationship(deleteRule: .cascade) var captures: [FreeBallCapture] = []
    init(startedAt: Date) { self.startedAt = startedAt }
}

@available(macOS 14, *)
@Model
final class FreeBallCapture {
    var firstSeenAt: Date    // when this screen first appeared
    var lastSeenAt: Date     // extended while the screen stays effectively unchanged
    var text: String         // full OCR text of the screen
    init(firstSeenAt: Date, lastSeenAt: Date, text: String) {
        self.firstSeenAt = firstSeenAt; self.lastSeenAt = lastSeenAt; self.text = text
    }
    /// How long this screen was up, in seconds.
    var seconds: TimeInterval { lastSeenAt.timeIntervalSince(firstSeenAt) }
}
```

**Step 5: Add to the schema** in `Persistence.swift` `makeContainer`:

```swift
        let schema = Schema([WorkSession.self, TimelineEntry.self, JustificationEvent.self,
                             KnowledgeTask.self, Allowance.self, TaskCompletion.self,
                             FreeBallSession.self, FreeBallCapture.self])
```

**Step 6: Run to verify it passes**

Run: `make -C src test`
Expected: PASS.

**Step 7: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): FreeBallSession/FreeBallCapture models + schema"
```

---

### Task 3: FreeBallDedup (same-screen detection)

Pure function deciding whether a new OCR read is "the same screen" as the previous stored block (→ extend its time range) or genuinely new (→ new block).

**Files:**
- Create: `src/Sources/AccountaBall/Util/FreeBallDedup.swift`
- Modify: `src/Sources/AccountaBall/Util/AppConstants.swift`
- Test: `src/Tests/AccountaBallTests/FreeBallDedupTests.swift`
- Modify: `src/Tests/TestRunner/main.swift`

**Step 1: Write the failing test** at `FreeBallDedupTests.swift`:

```swift
@testable import AccountaBall

func runFreeBallDedupTests() {
    suite("FreeBallDedup") {
        let a = "Editing AppDelegate.swift — func applicationDidFinishLaunching"
        let aClock = a + " 10:42"   // trivial change (clock tick)
        expect(FreeBallDedup.isSameScreen(a, aClock, threshold: 0.85), "near-identical = same screen")

        let b = "Watching a YouTube video about basketball highlights"
        expect(!FreeBallDedup.isSameScreen(a, b, threshold: 0.85), "different content = new screen")

        expect(FreeBallDedup.similarity("foo bar baz", "foo bar baz") == 1.0, "identical = 1.0")
        expect(FreeBallDedup.similarity("foo bar", "nothing here") < 0.2, "disjoint ~ 0")
    }
}
```

**Step 2: Register** in the runner sync block:

```swift
    runFreeBallDedupTests()
```

**Step 3: Run to verify it fails**

Run: `make -C src test`
Expected: FAIL — `FreeBallDedup` undefined.

**Step 4: Implement** at `FreeBallDedup.swift`:

```swift
import Foundation

/// Decide whether two consecutive OCR reads show effectively the same screen.
/// At a 3s cadence, adjacent cycles are usually near-identical (same window,
/// only a clock/cursor differs). Treating those as one block keeps the stored
/// transcript honest ("full text, full session") without thousands of dupes.
enum FreeBallDedup {
    /// Jaccard similarity over lowercased word sets. 1.0 = identical sets, 0 = disjoint.
    static func similarity(_ a: String, _ b: String) -> Double {
        let sa = Set(tokens(a)), sb = Set(tokens(b))
        if sa.isEmpty && sb.isEmpty { return 1.0 }
        let inter = sa.intersection(sb).count
        let union = sa.union(sb).count
        return union == 0 ? 0 : Double(inter) / Double(union)
    }

    static func isSameScreen(_ a: String, _ b: String, threshold: Double = AppConstants.freeBallDedupThreshold) -> Bool {
        similarity(a, b) >= threshold
    }

    private static func tokens(_ s: String) -> [String] {
        s.lowercased()
            .components(separatedBy: CharacterSet.alphanumerics.inverted)
            .filter { !$0.isEmpty }
    }
}
```

**Step 5: Add the constant** to `AppConstants.swift`:

```swift
    /// FreeBall: two consecutive OCR reads at or above this Jaccard word-set
    /// similarity are treated as the same screen (extend the block instead of
    /// writing a new one).
    static let freeBallDedupThreshold: Double = 0.85
```

**Step 6: Run to verify it passes**

Run: `make -C src test`
Expected: PASS.

**Step 7: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): FreeBallDedup same-screen detection"
```

---

### Task 4: FreeBallCondenser (transcript budgeting)

Pure function that caps the total characters fed to the summarizer, keeping the longest-lived screens fullest.

**Files:**
- Create: `src/Sources/AccountaBall/Util/FreeBallCondenser.swift`
- Modify: `src/Sources/AccountaBall/Util/AppConstants.swift`
- Test: `src/Tests/AccountaBallTests/FreeBallCondenserTests.swift`
- Modify: `src/Tests/TestRunner/main.swift`

> Note: `FreeBallTranscriptEntry` is defined in Task 5 (`FreeBallRecap.swift`). Do Task 5 first if you prefer; this plan orders the test to drive the type into existence there. To keep Task 4 self-contained, define `FreeBallTranscriptEntry` here in `FreeBallRecap.swift` as Step 0 and let Task 5 add the rest.

**Step 0: Create `src/Sources/AccountaBall/Models/FreeBallRecap.swift`** with just the transcript entry (Task 5 appends the other types):

```swift
import Foundation

/// One deduped block of the live session, fed to the summarizer.
struct FreeBallTranscriptEntry: Equatable {
    let text: String
    let seconds: TimeInterval   // how long this screen was up
}
```

**Step 1: Write the failing test** at `FreeBallCondenserTests.swift`:

```swift
@testable import AccountaBall

func runFreeBallCondenserTests() {
    suite("FreeBallCondenser") {
        let long = String(repeating: "x", count: 500)
        let short = String(repeating: "y", count: 500)
        let entries = [
            FreeBallTranscriptEntry(text: long, seconds: 600),   // long-lived
            FreeBallTranscriptEntry(text: short, seconds: 30)    // brief blip
        ]
        let out = FreeBallCondenser.condense(entries, maxChars: 300)
        let total = out.reduce(0) { $0 + $1.text.count }
        expect(total <= 300, "total trimmed under budget")
        expect(out.count == 2, "entries preserved in order")
        expect(out[0].text.count > out[1].text.count, "longer-lived screen kept fuller")

        let small = [FreeBallTranscriptEntry(text: "hi", seconds: 10)]
        expect(FreeBallCondenser.condense(small, maxChars: 1000) == small, "under budget = unchanged")
    }
}
```

**Step 2: Register** in the runner sync block: `runFreeBallCondenserTests()`

**Step 3: Run to verify it fails**

Run: `make -C src test`
Expected: FAIL — `FreeBallCondenser` undefined.

**Step 4: Implement** at `FreeBallCondenser.swift`:

```swift
import Foundation

/// Cap the transcript size fed to a local model. Budget is split across entries
/// in proportion to how long each screen was up, so the longest-lived screens
/// keep the most text and brief blips are trimmed first. Chronological order is
/// preserved. (Chunk-then-stitch for extreme sessions is a future refinement.)
enum FreeBallCondenser {
    static func condense(_ entries: [FreeBallTranscriptEntry],
                         maxChars: Int = AppConstants.freeBallMaxTranscriptChars) -> [FreeBallTranscriptEntry] {
        let total = entries.reduce(0) { $0 + $1.text.count }
        if total <= maxChars { return entries }
        let weightTotal = entries.reduce(0.0) { $0 + max($1.seconds, 1) }
        return entries.map { e in
            let share = max(e.seconds, 1) / weightTotal
            let budget = max(40, Int(Double(maxChars) * share))   // floor so nothing vanishes
            let trimmed = e.text.count <= budget ? e.text : String(e.text.prefix(budget))
            return FreeBallTranscriptEntry(text: trimmed, seconds: e.seconds)
        }
    }
}
```

**Step 5: Add constants** to `AppConstants.swift`:

```swift
    /// FreeBall: max characters of deduped transcript fed to the summarizer.
    static let freeBallMaxTranscriptChars: Int = 12000
    /// FreeBall: a session with less total captured text than this is "trivial" —
    /// skip the AI call and show a gentle not-enough-yet recap.
    static let freeBallMinCharsToSummarize: Int = 40
    /// FreeBall: how many recent past-session recaps to feed as cross-session context.
    static let freeBallPastRecapCap: Int = 10
```

**Step 6: Run to verify it passes**

Run: `make -C src test`
Expected: PASS.

**Step 7: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): FreeBallCondenser transcript budgeting + constants"
```

---

### Task 5: FreeBall summary types + prompt build/parse

The `AIService` method's input/output types + the testable prompt builder and JSON parser (network-free), mirroring `summarizeSession` in `AIPrompts.swift`.

**Files:**
- Modify: `src/Sources/AccountaBall/Models/FreeBallRecap.swift` (append types)
- Modify: `src/Sources/AccountaBall/Util/AIPrompts.swift`
- Test: `src/Tests/AccountaBallTests/FreeBallSummarizeTests.swift`
- Modify: `src/Tests/TestRunner/main.swift`

**Step 1: Append the remaining types** to `FreeBallRecap.swift`:

```swift
/// A prior session's distilled recap, fed as cross-session context (recaps, not
/// raw transcripts, so context stays small as history grows).
struct FreeBallPastRecap: Equatable {
    let narrative: String
    let categories: [CategorySpan]
    let insight: String
}

/// The AI's structured output for one ended session.
struct FreeBallSummary: Equatable {
    let narrative: String
    let categories: [CategorySpan]
    let insight: String
}

/// UI-facing recap published to AppState and rendered by FreeBallRecapView.
struct FreeBallRecap: Equatable {
    let duration: TimeInterval
    let narrative: String
    let categories: [CategorySpan]
    let insight: String
    let recapPending: Bool   // AI was unavailable at End Session; raw kept for later
}
```

**Step 2: Write the failing test** at `FreeBallSummarizeTests.swift`:

```swift
@testable import AccountaBall

func runFreeBallSummarizeTests() {
    suite("FreeBallSummarize_parse") {
        let json = #"{"narrative":"You mostly coded.","categories":[{"label":"Coding","minutes":45},{"label":"Email","minutes":10}],"insight":"You code in long blocks."}"#
        let out = AIPrompts.parseFreeBallSummary(json)
        expect(out.narrative == "You mostly coded.", "narrative parsed")
        expect(out.categories.count == 2, "two categories")
        expect(out.categories.first?.label == "Coding", "first label")
        expect(out.categories.first?.minutes == 45, "first minutes")
        expect(out.insight == "You code in long blocks.", "insight parsed")
    }

    suite("FreeBallSummarize_parse_badJSON") {
        let out = AIPrompts.parseFreeBallSummary("not json")
        expect(out.narrative.isEmpty, "bad json -> empty narrative")
        expect(out.categories.isEmpty, "bad json -> empty categories")
    }

    suite("FreeBallSummarize_prompt") {
        let transcript = [FreeBallTranscriptEntry(text: "editing main.swift", seconds: 120)]
        let past = [FreeBallPastRecap(narrative: "Lots of email.", categories: [CategorySpan(label: "Email", minutes: 30)], insight: "Mornings are emaily.")]
        let prompt = AIPrompts.buildFreeBallPrompt(transcript: transcript, pastRecaps: past)
        expect(prompt.contains("editing main.swift"), "transcript text included")
        expect(prompt.contains("Lots of email."), "past recap included")
    }
}
```

**Step 3: Register** in the runner sync block: `runFreeBallSummarizeTests()`

**Step 4: Run to verify it fails**

Run: `make -C src test`
Expected: FAIL — `AIPrompts.parseFreeBallSummary` / `buildFreeBallPrompt` undefined.

**Step 5: Add to `AIPrompts.swift`** (inside the `extension AIPrompts` block, beside `buildSessionPrompt`):

```swift
    static let freeBallSystem = """
    You are observing how a user spends a work session. You are NOT judging whether
    they stayed on task — there is no declared task. Read the transcript of what was
    on their screen (each block notes roughly how long that screen was up), and any
    summaries of their PAST sessions, then describe where their time actually went.
    Respond with JSON only:
    {"narrative":"<2-4 sentences, plain language, what they spent the session on>",
     "categories":[{"label":"<activity, e.g. Coding>","minutes":<int>}, ...],
     "insight":"<one observation about their habits; you MAY reference the past
                 sessions, e.g. 'You usually switch to email when stuck'>"}
    Categories should sum roughly to the session length. Output JSON only, no prose.
    """

    /// Build the user-side prompt: this session's deduped transcript + capped past recaps.
    static func buildFreeBallPrompt(transcript: [FreeBallTranscriptEntry],
                                    pastRecaps: [FreeBallPastRecap]) -> String {
        let body = transcript.map { e in
            "[\(Int((e.seconds/60).rounded()))m on screen]\n\(e.text)"
        }.joined(separator: "\n\n---\n\n")
        var prompt = "This session — what was on screen:\n\n\(body)"
        if !pastRecaps.isEmpty {
            let pastBlock = pastRecaps.enumerated().map { i, r in
                let cats = r.categories.map { "\($0.label) \($0.minutes)m" }.joined(separator: ", ")
                return "Session \(i + 1): \(r.narrative) [\(cats)] Insight: \(r.insight)"
            }.joined(separator: "\n")
            prompt += "\n\n---\n\nYour past sessions (for the insight; do not re-summarize them):\n\(pastBlock)"
        }
        return prompt
    }

    /// Parse the AI's JSON into a FreeBallSummary. Returns an empty summary on any failure.
    static func parseFreeBallSummary(_ json: String) -> FreeBallSummary {
        guard let open = json.firstIndex(of: "{"), let close = json.lastIndex(of: "}"),
              let data = String(json[open...close]).data(using: .utf8),
              let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return FreeBallSummary(narrative: "", categories: [], insight: "")
        }
        let narrative = (obj["narrative"] as? String) ?? ""
        let insight = (obj["insight"] as? String) ?? ""
        let cats: [CategorySpan] = ((obj["categories"] as? [[String: Any]]) ?? []).compactMap { d in
            guard let label = d["label"] as? String else { return nil }
            let mins = (d["minutes"] as? Int) ?? Int((d["minutes"] as? Double) ?? 0)
            return CategorySpan(label: label, minutes: mins)
        }
        return FreeBallSummary(narrative: narrative, categories: cats, insight: insight)
    }
```

**Step 6: Run to verify it passes**

Run: `make -C src test`
Expected: PASS.

**Step 7: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): summary types + buildFreeBallPrompt/parseFreeBallSummary"
```

---

### Task 6: AIService.summarizeFreeBall — protocol + all conformers

Adding the protocol method breaks every conformer (compiler-enforced). Implement real providers via the shared builder/parser; stub the legacy + test fakes.

**Files:**
- Modify: `src/Sources/AccountaBall/Services/AIService.swift`
- Modify: `src/Sources/AccountaBall/Services/OllamaAIService.swift`
- Modify: `src/Sources/AccountaBall/Services/OpenRouterAIService.swift`
- Modify: `src/Sources/AccountaBall/Services/ClaudeAIService.swift`
- Modify: `src/Tests/AccountaBallTests/TestFakes.swift`
- Modify: every other test file that declares `: AIService` (see Step 1)

**Step 1: Find all conformers** so none is missed:

Run: `grep -rln ": AIService" src/Sources src/Tests`
Expected: `AIService.swift`, the 3 services, `TestFakes.swift`, and several `Engine*Tests.swift` files (e.g. `AccountabilityEngineTests`, `EngineAllowanceTests`, `EngineCompletionTests`, `EngineMatchTests`, and any error/availability/settle/prompt-gate fakes).

**Step 2: Add to the protocol** in `AIService.swift`:

```swift
    /// Passive-mode summary. Reads this session's deduped transcript plus distilled
    /// past-session recaps; returns a narrative + categorized time breakdown +
    /// cross-session insight. The ONLY AI call FreeBall makes (at End Session).
    func summarizeFreeBall(transcript: [FreeBallTranscriptEntry],
                           pastRecaps: [FreeBallPastRecap]) async throws -> FreeBallSummary
```

**Step 3: Run to verify it fails to compile**

Run: `make -C src test`
Expected: FAIL — every conformer is now missing `summarizeFreeBall`.

**Step 4: Implement on Ollama** (`OllamaAIService.swift`, beside `summarizeSession`):

```swift
    func summarizeFreeBall(transcript: [FreeBallTranscriptEntry],
                           pastRecaps: [FreeBallPastRecap]) async throws -> FreeBallSummary {
        let prompt = AIPrompts.buildFreeBallPrompt(transcript: transcript, pastRecaps: pastRecaps)
        let schema: [String: Any] = [
            "type": "object",
            "properties": [
                "narrative": ["type": "string"],
                "categories": ["type": "array", "items": ["type": "object",
                    "properties": ["label": ["type": "string"], "minutes": ["type": "integer"]],
                    "required": ["label", "minutes"]]],
                "insight": ["type": "string"]
            ],
            "required": ["narrative", "categories", "insight"]
        ]
        let raw: String = try await send(system: AIPrompts.freeBallSystem, user: prompt, schema: schema)
        return AIPrompts.parseFreeBallSummary(raw)
    }
```

**Step 5: Implement on OpenRouter** (`OpenRouterAIService.swift`, beside `summarizeSession`):

```swift
    func summarizeFreeBall(transcript: [FreeBallTranscriptEntry],
                           pastRecaps: [FreeBallPastRecap]) async throws -> FreeBallSummary {
        let prompt = AIPrompts.buildFreeBallPrompt(transcript: transcript, pastRecaps: pastRecaps)
        let raw = try await sendMessage(system: AIPrompts.freeBallSystem, user: prompt,
                                        maxTokens: 700, responseFormatJSON: true)
        return AIPrompts.parseFreeBallSummary(raw)
    }
```

**Step 6: Stub the legacy provider** (`ClaudeAIService.swift`) — FreeBall targets Ollama/OpenRouter; keep Claude compiling:

```swift
    func summarizeFreeBall(transcript: [FreeBallTranscriptEntry],
                           pastRecaps: [FreeBallPastRecap]) async throws -> FreeBallSummary {
        FreeBallSummary(narrative: "", categories: [], insight: "")
    }
```

**Step 7: Add to `AlwaysOnTaskAI`** in `TestFakes.swift`:

```swift
    func summarizeFreeBall(transcript: [FreeBallTranscriptEntry], pastRecaps: [FreeBallPastRecap]) async throws -> FreeBallSummary { FreeBallSummary(narrative: "", categories: [], insight: "") }
```

**Step 8: Add the same stub line to every other test fake** found in Step 1 (MultiMockAI, VerdictFakeAI, FixedRecapAI, NoMatchAI, and any others). Use the identical one-line stub.

**Step 9: Run to verify the suite passes**

Run: `make -C src test`
Expected: PASS (all existing suites green again).

**Step 10: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): AIService.summarizeFreeBall on all providers + fakes"
```

---

### Task 7: Extract shared screen-text builder

`AccountabilityEngine.screenText(from:)` builds focused+peripheral OCR text. FreeBall needs the same. Extract to a shared free function (DRY) and delegate.

**Files:**
- Create: `src/Sources/AccountaBall/Util/ScreenText.swift`
- Modify: `src/Sources/AccountaBall/Engine/AccountabilityEngine.swift:134-144`

> No new unit test: this is a mechanical extraction over OCR/CGImage (not unit-testable without real images). The existing full suite + build are the safety net — `AccountabilityEngine` behavior must be unchanged.

**Step 1: Create** `ScreenText.swift`:

```swift
import Foundation

/// Build the text we OCR from one capture tick: the focused window in full
/// (primary), plus a truncated pass over the whole screen as lighter peripheral
/// context. With no focused window, the full screen is the only signal. Shared by
/// AccountabilityEngine (classify input) and FreeBallEngine (stored transcript).
func buildScreenText(from frame: CapturedFrame, ocr: OCRService,
                     peripheralChars: Int = AppConstants.peripheralScreenChars) async -> String {
    guard let focused = frame.focused else {
        return await ocr.extractText(from: frame.full)
    }
    let primary = await ocr.extractText(from: focused)
    let full = await ocr.extractText(from: frame.full)
    let light = String(full.prefix(peripheralChars))
    if primary.isEmpty { return light }
    if light.isEmpty { return primary }
    return "Active window:\n\(primary)\n\nAlso visible on screen:\n\(light)"
}
```

**Step 2: Replace the body** of `AccountabilityEngine.screenText(from:)` to delegate:

```swift
    func screenText(from frame: CapturedFrame) async -> String {
        await buildScreenText(from: frame, ocr: ocrService)
    }
```

**Step 3: Build + test to verify nothing changed**

Run: `make -C src build && make -C src test`
Expected: build OK; all suites PASS.

**Step 4: Commit**

```bash
cd src && git add -A && git commit -m "refactor: extract shared buildScreenText() for reuse by FreeBall"
```

---

### Task 8: AppState FreeBall bridges

**Files:**
- Modify: `src/Sources/AccountaBall/Models/AppState.swift`
- Test: `src/Tests/AccountaBallTests/AppStateV2Tests.swift` (append a suite) OR a new `FreeBallStateTests.swift`

**Step 1: Write a failing test.** Append to `AppStateV2Tests.swift` inside its function (or create `FreeBallStateTests.swift` + register it):

```swift
    suite("AppState_freeBall") {
        let s = AppState()
        expect(s.freeBallStartTime == nil, "no FreeBall start by default")
        expect(s.freeBallRecap == nil, "no FreeBall recap by default")
        expect(s.freeBallSummarizing == false, "not summarizing by default")
    }
```

**Step 2: Run to verify it fails**

Run: `make -C src test`
Expected: FAIL — members undefined.

**Step 3: Add to `AppState`** (beside the other `@Published` properties):

```swift
    // FreeBall — passive observation mode bridges.
    /// Wall-clock start of the active FreeBall session (drives the live timer).
    @Published var freeBallStartTime: Date? = nil
    /// True while End Session is awaiting the summary (recap view shows a loading state).
    @Published var freeBallSummarizing: Bool = false
    /// The finished FreeBall recap, rendered by FreeBallRecapView.
    @Published var freeBallRecap: FreeBallRecap? = nil
```

**Step 4: Run to verify it passes**

Run: `make -C src test`
Expected: PASS.

**Step 5: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): AppState bridges (start time, summarizing, recap)"
```

---

### Task 9: FreeBallEngine (begin / ingest / end)

The heart of the feature. `@MainActor`, owns its own capture loop, no AI until `end()`.

**Files:**
- Create: `src/Sources/AccountaBall/Engine/FreeBallEngine.swift`
- Test: `src/Tests/AccountaBallTests/FreeBallEngineTests.swift`
- Modify: `src/Tests/TestRunner/main.swift` (async section)

**Step 1: Write failing tests** at `FreeBallEngineTests.swift`. These drive `ingest`/`end` directly (no real capture loop), with controllable fake AIs.

```swift
import SwiftData
@testable import AccountaBall

/// Fake returning a fixed FreeBall summary; records that it was called.
final class FreeBallFakeAI: AIService {
    var called = false
    var summary = FreeBallSummary(narrative: "N", categories: [CategorySpan(label: "Coding", minutes: 5)], insight: "I")
    func classify(task: String, screenText: String) async throws -> BallState { .onTask }
    func classifyMulti(tasks: [TaskItem], screenText: String, allowanceRulesByIndex: [Int: [String]]) async throws -> MultiTaskResult { .onTask(index: 0, label: "") }
    func evaluateExcuse(excuse: String, tasks: [TaskItem], screenText: String) async throws -> ExcuseVerdict { ExcuseVerdict(justified: false, taskIndex: nil, rule: "") }
    func summarizeTask(title: String, context: String, steps: [String], durationSeconds: TimeInterval, previous: (durationSeconds: TimeInterval, steps: [String], offTaskCount: Int)?) async throws -> TaskRecap { TaskRecap(summary: "", steps: [], duration: durationSeconds, comparison: nil) }
    func matchTask(query: String, candidates: [(id: String, title: String, summary: String)]) async throws -> (id: String, confident: Bool)? { nil }
    func healthCheck() async -> Bool { true }
    func summarizeSession(perTask: [PerTaskSessionInput]) async throws -> [PerTaskComment] { [] }
    func summarizeFreeBall(transcript: [FreeBallTranscriptEntry], pastRecaps: [FreeBallPastRecap]) async throws -> FreeBallSummary { called = true; return summary }
}

/// Fake whose summarizeFreeBall throws (provider unreachable at End Session).
final class FreeBallFailAI: AIService {
    func classify(task: String, screenText: String) async throws -> BallState { .onTask }
    func classifyMulti(tasks: [TaskItem], screenText: String, allowanceRulesByIndex: [Int: [String]]) async throws -> MultiTaskResult { .onTask(index: 0, label: "") }
    func evaluateExcuse(excuse: String, tasks: [TaskItem], screenText: String) async throws -> ExcuseVerdict { ExcuseVerdict(justified: false, taskIndex: nil, rule: "") }
    func summarizeTask(title: String, context: String, steps: [String], durationSeconds: TimeInterval, previous: (durationSeconds: TimeInterval, steps: [String], offTaskCount: Int)?) async throws -> TaskRecap { TaskRecap(summary: "", steps: [], duration: durationSeconds, comparison: nil) }
    func matchTask(query: String, candidates: [(id: String, title: String, summary: String)]) async throws -> (id: String, confident: Bool)? { nil }
    func healthCheck() async -> Bool { false }
    func summarizeSession(perTask: [PerTaskSessionInput]) async throws -> [PerTaskComment] { [] }
    func summarizeFreeBall(transcript: [FreeBallTranscriptEntry], pastRecaps: [FreeBallPastRecap]) async throws -> FreeBallSummary { throw URLError(.cannotConnectToHost) }
}

@MainActor
private func makeFreeBallEngine(ai: AIService) -> (FreeBallEngine, AppState, ModelContext) {
    let state = AppState()
    let container = try! AccountaBallStore.makeContainer(inMemory: true)
    let eng = FreeBallEngine(state: state, captureService: ScreenCaptureService(),
                             ocrService: OCRService(), aiService: ai)
    eng.modelContext = container.mainContext
    return (eng, state, container.mainContext)
}

@MainActor
func runFreeBallEngineTests() async {
    suite("FreeBallEngine_begin") {
        let (eng, state, _) = makeFreeBallEngine(ai: FreeBallFakeAI())
        eng.beginForTest()   // begin without starting the real capture loop
        expect(state.appPhase == .freeBall, "begin -> .freeBall")
        expect(state.freeBallStartTime != nil, "start time set")
        expect(eng.currentSession != nil, "session created")
    }

    suite("FreeBallEngine_ingest_dedup") {
        let (eng, _, ctx) = makeFreeBallEngine(ai: FreeBallFakeAI())
        eng.beginForTest()
        eng.ingest(text: "editing AppDelegate.swift line 1")
        eng.ingest(text: "editing AppDelegate.swift line 1 ")   // same screen -> extend
        eng.ingest(text: "watching youtube basketball video")   // new screen -> new block
        let caps = (try? ctx.fetch(FetchDescriptor<FreeBallCapture>())) ?? []
        expect(caps.count == 2, "dedup collapsed identical consecutive reads")
    }

    suite("FreeBallEngine_end_summarizes") {
        let ai = FreeBallFakeAI()
        let (eng, state, _) = makeFreeBallEngine(ai: ai)
        eng.beginForTest()
        eng.ingest(text: String(repeating: "real work content ", count: 10))
        await eng.end()
        expect(ai.called, "summarizeFreeBall called at end")
        expect(state.appPhase == .freeBallRecap, "end -> .freeBallRecap")
        expect(state.freeBallRecap?.narrative == "N", "recap published")
        expect(state.freeBallSummarizing == false, "summarizing cleared")
        expect(eng.currentSession == nil, "session closed")
    }

    suite("FreeBallEngine_end_emptySession") {
        let ai = FreeBallFakeAI()
        let (eng, state, _) = makeFreeBallEngine(ai: ai)
        eng.beginForTest()           // no ingest -> trivial session
        await eng.end()
        expect(!ai.called, "trivial session skips the AI call")
        expect(state.appPhase == .freeBallRecap, "still shows a recap")
    }

    suite("FreeBallEngine_end_aiUnavailable") {
        let (eng, state, ctx) = makeFreeBallEngine(ai: FreeBallFailAI())
        eng.beginForTest()
        eng.ingest(text: String(repeating: "real work content ", count: 10))
        await eng.end()
        expect(state.freeBallRecap?.recapPending == true, "recap marked pending on AI failure")
        let sessions = (try? ctx.fetch(FetchDescriptor<FreeBallSession>())) ?? []
        expect(sessions.first?.recapPending == true, "session persisted recapPending")
        expect(sessions.first?.captures.isEmpty == false, "raw captures retained for later")
    }
}
```

**Step 2: Register** in `src/Tests/TestRunner/main.swift` async section (after the other `await runEngine…`):

```swift
    await runFreeBallEngineTests()
```

**Step 3: Run to verify it fails**

Run: `make -C src test`
Expected: FAIL — `FreeBallEngine` undefined.

**Step 4: Implement** `FreeBallEngine.swift`:

```swift
import Foundation
import SwiftData
import AppKit

/// Passive observation mode. Captures full screen text every cycle, dedups
/// consecutive near-identical reads, and stores everything. Runs NO AI until
/// end(), which assembles the transcript + past recaps and makes one
/// summarizeFreeBall call.
@MainActor
final class FreeBallEngine {
    private let state: AppState
    private let captureService: ScreenCaptureService
    private let ocrService: OCRService
    private let aiService: AIService
    private var captureTask: Task<Void, Never>?
    private var appNapToken: NSObjectProtocol?

    var modelContext: ModelContext?
    private(set) var currentSession: FreeBallSession?

    /// Injectable clock (tests extend ranges without sleeping).
    var now: () -> Date = { Date() }

    init(state: AppState, captureService: ScreenCaptureService,
         ocrService: OCRService, aiService: AIService) {
        self.state = state
        self.captureService = captureService
        self.ocrService = ocrService
        self.aiService = aiService
    }

    // MARK: - lifecycle

    /// Start a session: create the record, set phase/start time, prevent App Nap,
    /// and start the silent capture loop.
    func begin() {
        beginSessionRecord()
        if appNapToken == nil {
            appNapToken = ProcessInfo.processInfo.beginActivity(options: [.userInitiated], reason: "AccountaBall FreeBall session")
        }
        captureTask = captureService.startLoop(interval: AppConstants.cycleSeconds, panelTitle: "AccountaBall") { [weak self] frame in
            guard let self else { return }
            let text = await buildScreenText(from: frame, ocr: self.ocrService)
            guard !text.isEmpty else { return }
            await self.ingest(text: text)
        }
    }

    /// Test seam: set up a session without starting the real capture loop.
    func beginForTest() { beginSessionRecord() }

    private func beginSessionRecord() {
        guard let ctx = modelContext else { return }
        let session = FreeBallSession(startedAt: now())
        ctx.insert(session)
        try? ctx.save()
        currentSession = session
        state.freeBallStartTime = session.startedAt
        state.freeBallRecap = nil
        state.appPhase = .freeBall
    }

    /// Ingest one OCR read: extend the last block if it's the same screen, else
    /// open a new block. Never calls the AI.
    func ingest(text: String) {
        guard let session = currentSession, let ctx = modelContext else { return }
        session.cycleCount += 1
        if let last = session.captures.max(by: { $0.lastSeenAt < $1.lastSeenAt }),
           FreeBallDedup.isSameScreen(last.text, text) {
            last.lastSeenAt = now()
        } else {
            let cap = FreeBallCapture(firstSeenAt: now(), lastSeenAt: now(), text: text)
            ctx.insert(cap)
            session.captures.append(cap)
        }
        try? ctx.save()
    }

    /// Internal stop: cancel the loop and release the App Nap token.
    private func stop() {
        captureTask?.cancel(); captureTask = nil
        if let t = appNapToken { ProcessInfo.processInfo.endActivity(t); appNapToken = nil }
    }

    /// End the session: stop capturing, show the recap with a loading state, then
    /// make the single AI summarize call. Degrades gracefully if the AI is down.
    func end() async {
        stop()
        guard let session = currentSession, let ctx = modelContext else { return }
        session.endedAt = now()
        let duration = session.endedAt!.timeIntervalSince(session.startedAt)

        state.freeBallSummarizing = true
        state.appPhase = .freeBallRecap

        // Assemble transcript (chronological), condensed to the model's budget.
        let entries = session.captures
            .sorted { $0.firstSeenAt < $1.firstSeenAt }
            .map { FreeBallTranscriptEntry(text: $0.text, seconds: max($0.seconds, AppConstants.cycleSeconds)) }
        let totalChars = entries.reduce(0) { $0 + $1.text.count }

        // Trivial session: skip the AI, show a gentle recap.
        if totalChars < AppConstants.freeBallMinCharsToSummarize {
            session.narrative = "Not enough captured to summarize yet."
            try? ctx.save()
            publishRecap(duration: duration, summary:
                FreeBallSummary(narrative: session.narrative, categories: [], insight: ""),
                pending: false)
            finishEnd()
            return
        }

        let condensed = FreeBallCondenser.condense(entries)
        let pastRecaps = fetchPastRecaps(before: session)

        do {
            let summary = try await aiService.summarizeFreeBall(transcript: condensed, pastRecaps: pastRecaps)
            session.narrative = summary.narrative
            session.categories = summary.categories
            session.insight = summary.insight
            session.recapPending = false
            try? ctx.save()
            publishRecap(duration: duration, summary: summary, pending: false)
        } catch {
            dbg("summarizeFreeBall failed: \(error)")
            session.recapPending = true     // keep raw captures for a later pass
            try? ctx.save()
            state.setupHint = "Couldn't summarize this FreeBall session — the AI was unreachable. Your capture was saved."
            publishRecap(duration: duration,
                summary: FreeBallSummary(narrative: "", categories: [], insight: ""),
                pending: true)
        }
        finishEnd()
    }

    private func finishEnd() {
        state.freeBallSummarizing = false
        currentSession = nil
    }

    private func publishRecap(duration: TimeInterval, summary: FreeBallSummary, pending: Bool) {
        state.freeBallRecap = FreeBallRecap(
            duration: duration, narrative: summary.narrative,
            categories: summary.categories, insight: summary.insight, recapPending: pending)
    }

    /// Most recent completed recaps (excluding this session), capped, as cross-session context.
    private func fetchPastRecaps(before session: FreeBallSession) -> [FreeBallPastRecap] {
        guard let ctx = modelContext else { return [] }
        let all = (try? ctx.fetch(FetchDescriptor<FreeBallSession>())) ?? []
        return all
            .filter { $0.id != session.id && $0.endedAt != nil && !$0.recapPending && !$0.narrative.isEmpty }
            .sorted { ($0.endedAt ?? .distantPast) > ($1.endedAt ?? .distantPast) }
            .prefix(AppConstants.freeBallPastRecapCap)
            .map { FreeBallPastRecap(narrative: $0.narrative, categories: $0.categories, insight: $0.insight) }
    }
}
```

> Note on the dedup test: `ingest` finds the latest block by `lastSeenAt`; identical consecutive text extends it (no new row), differing text opens a new one — matching the test's expectation of 2 rows from 3 reads.

**Step 5: Run to verify it passes**

Run: `make -C src test`
Expected: PASS (including the 5 new FreeBallEngine suites).

**Step 6: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): FreeBallEngine begin/ingest/end with summarization"
```

---

### Task 10: Calm ball face

The observing ball needs a neutral face (eyes + flat mouth), distinct from happy/angry.

**Files:**
- Modify: `src/Sources/AccountaBall/Views/BasketballView.swift`

> UI-only; verified by build (no unit test for SwiftUI drawing).

**Step 1: Add `.calm`** to the `BallFace` enum:

```swift
    enum BallFace {
        case none, happy, angry, calm
    }
```

**Step 2: Render a flat mouth for `.calm`** in `faceOverlay`. Replace the mouth `Path` block so `.calm` draws a short horizontal line instead of an arc:

```swift
            if showFace == .calm {
                Rectangle()
                    .fill(Color.black)
                    .frame(width: size * 0.30, height: max(1.5, size / 30))
            } else {
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
```

**Step 3: Build**

Run: `make -C src build`
Expected: build OK.

**Step 4: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): calm ball face for observing mode"
```

---

### Task 11: FreeBall views (ball, log, recap)

Replace the Task 1 placeholders with real views.

**Files:**
- Create: `src/Sources/AccountaBall/Views/FreeBallView.swift` (all three views in one file)
- Modify: `src/Sources/AccountaBall/Views/RootCoordinatorView.swift`

> UI; verified by build + the manual run in Task 14.

**Step 1: Create** `FreeBallView.swift`:

```swift
import SwiftUI

/// Collapsed calm ball during a FreeBall session. Tapping opens the live log.
struct FreeBallBallView: View {
    @EnvironmentObject var state: AppState
    @State private var seconds: Int = 0
    private let timer = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    var body: some View {
        ZStack {
            BasketballView(size: 80, showFace: .calm)
            Text(elapsed)
                .font(.system(size: 11, weight: .bold, design: .monospaced))
                .foregroundStyle(.white)
                .offset(y: 34)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .contentShape(Rectangle())
        .onTapGesture { withAnimation { state.appPhase = .freeBallLog } }
        .onReceive(timer) { _ in seconds += 1 }
    }

    private var elapsed: String { formatElapsed(since: state.freeBallStartTime) }
}

/// Live session log: timer + End Session. No content shown (nothing analyzed yet).
struct FreeBallSessionView: View {
    @EnvironmentObject var state: AppState
    var engine: FreeBallEngine
    @State private var seconds: Int = 0
    private let timer = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    var body: some View {
        VStack(spacing: 16) {
            BasketballView(size: 56, showFace: .calm)
            Text(formatElapsed(since: state.freeBallStartTime))
                .font(.system(size: 28, weight: .bold, design: .monospaced))
                .foregroundStyle(.white)
                .onReceive(timer) { _ in seconds += 1 }
            Text("watching…")
                .font(.caption)
                .foregroundStyle(.white.opacity(0.5))
            Button("End Session") {
                Task { await engine.end() }
            }
            .buttonStyle(PrimaryButtonStyle())
            Button("back") { withAnimation { state.appPhase = .freeBall } }
                .font(.caption).foregroundStyle(.white.opacity(0.6)).buttonStyle(.plain)
        }
        .padding(24)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color.black)
    }
}

/// Post-session recap: narrative + categorized breakdown + insight.
struct FreeBallRecapView: View {
    @EnvironmentObject var state: AppState
    var onDone: () -> Void

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()
            if state.freeBallSummarizing {
                VStack(spacing: 12) {
                    BasketballView(size: 56, showFace: .calm)
                    Text("making sense of your session…")
                        .font(.system(size: 14)).foregroundStyle(.white.opacity(0.7))
                }
            } else if let recap = state.freeBallRecap {
                content(recap)
            }
        }
    }

    @ViewBuilder private func content(_ recap: FreeBallRecap) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("Here's where your time went")
                    .font(.system(size: 18, weight: .bold)).foregroundStyle(.white)
                Text(formatElapsed(seconds: Int(recap.duration)))
                    .font(.caption).foregroundStyle(.white.opacity(0.5))

                if recap.recapPending {
                    Text("Couldn't reach the AI to summarize — your session was saved and can be summarized later.")
                        .font(.system(size: 13)).foregroundStyle(.orange)
                } else {
                    if !recap.narrative.isEmpty {
                        Text(recap.narrative)
                            .font(.system(size: 14)).foregroundStyle(.white)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    if !recap.categories.isEmpty {
                        breakdown(recap.categories)
                    }
                    if !recap.insight.isEmpty {
                        VStack(alignment: .leading, spacing: 4) {
                            Text("what FreeBall noticed").font(.caption).foregroundStyle(.white.opacity(0.5))
                            Text(recap.insight).font(.system(size: 13)).foregroundStyle(.white.opacity(0.85))
                                .fixedSize(horizontal: false, vertical: true)
                        }
                        .padding(12)
                        .background(Color.white.opacity(0.06))
                        .clipShape(RoundedRectangle(cornerRadius: 10))
                    }
                }

                Button("Done", action: onDone)
                    .buttonStyle(PrimaryButtonStyle())
                    .frame(maxWidth: .infinity)
            }
            .padding(24)
        }
    }

    @ViewBuilder private func breakdown(_ cats: [CategorySpan]) -> some View {
        let sorted = cats.sorted { $0.minutes > $1.minutes }
        let maxMin = max(sorted.first?.minutes ?? 1, 1)
        VStack(alignment: .leading, spacing: 8) {
            ForEach(Array(sorted.enumerated()), id: \.offset) { _, c in
                HStack {
                    Text(c.label).font(.system(size: 13)).foregroundStyle(.white).frame(width: 120, alignment: .leading)
                    GeometryReader { geo in
                        RoundedRectangle(cornerRadius: 4)
                            .fill(Color.orange)
                            .frame(width: geo.size.width * CGFloat(c.minutes) / CGFloat(maxMin))
                    }
                    .frame(height: 12)
                    Text("\(c.minutes)m").font(.system(size: 12, design: .monospaced)).foregroundStyle(.white.opacity(0.7))
                }
            }
        }
    }
}

// MARK: - shared formatting

func formatElapsed(seconds: Int) -> String {
    let h = seconds / 3600, m = (seconds % 3600) / 60, s = seconds % 60
    return h > 0 ? String(format: "%02d:%02d:%02d", h, m, s) : String(format: "%02d:%02d", m, s)
}
func formatElapsed(since start: Date?) -> String {
    guard let start else { return "00:00" }
    return formatElapsed(seconds: Int(Date().timeIntervalSince(start)))
}
```

**Step 2: Wire into `RootCoordinatorView`.** Replace the placeholder branch with real cases (the view needs `freeBallEngine` — added in Task 12/13; for now reference `freeBallEngine` and accept it won't compile until Task 13 adds the parameter — OR do Task 13 first). Replace:

```swift
            case .freeBall:
                FreeBallBallView()
                    .transition(.opacity)
            case .freeBallLog:
                FreeBallSessionView(engine: freeBallEngine)
                    .transition(.opacity)
            case .freeBallRecap:
                FreeBallRecapView(onDone: {
                    state.loadTasks()
                    withAnimation { state.appPhase = .welcome }
                })
                .transition(.opacity)
```

> `freeBallEngine` is added to `RootCoordinatorView` in Task 13. If building now fails on the missing parameter, proceed to Task 13 then return to build. (Recommended: implement Task 13 immediately after this step, then build once.)

**Step 3: Build** (after Task 13 adds the parameter)

Run: `make -C src build`
Expected: build OK.

**Step 4: Commit** (combined with Task 13 if built together)

```bash
cd src && git add -A && git commit -m "feat(freeball): ball/log/recap views"
```

---

### Task 12: WelcomeView FreeBall button

**Files:**
- Modify: `src/Sources/AccountaBall/Views/WelcomeView.swift`

**Step 1: Give `WelcomeView` the engine.** Add a stored property:

```swift
struct WelcomeView: View {
    @EnvironmentObject var state: AppState
    var freeBallEngine: FreeBallEngine
    // …existing @State…
```

**Step 2: Add the secondary button** below the primary CTA (after the `Button("Let's get started")` block, inside the same `VStack(spacing: 12)`):

```swift
                        Button("Let's get started") {
                            withAnimation { state.appPhase = .setup }
                        }
                        .buttonStyle(PrimaryButtonStyle())

                        Button("FreeBall") {
                            freeBallEngine.begin()
                        }
                        .buttonStyle(SecondaryButtonStyle())
```

**Step 3: Add a `SecondaryButtonStyle`** at the bottom of `WelcomeView.swift` (ghost/outline, subordinate to the primary):

```swift
struct SecondaryButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 14, weight: .medium))
            .foregroundStyle(.orange)
            .padding(.horizontal, 24)
            .padding(.vertical, 10)
            .background(RoundedRectangle(cornerRadius: 10).stroke(Color.orange, lineWidth: 1.5))
            .scaleEffect(configuration.isPressed ? 0.96 : 1)
    }
}
```

**Step 4: Build** (after Task 13 wires the engine through). Commit with Task 13.

---

### Task 13: AppDelegate wiring + dangling-session recovery

**Files:**
- Modify: `src/Sources/AccountaBall/AppDelegate.swift`
- Modify: `src/Sources/AccountaBall/Views/RootCoordinatorView.swift` (add `freeBallEngine` param)

**Step 1: Add the parameter to `RootCoordinatorView`:**

```swift
struct RootCoordinatorView: View {
    @EnvironmentObject var state: AppState
    var engine: AccountabilityEngine
    var freeBallEngine: FreeBallEngine
    var aiService: AIService
```

And pass it to `WelcomeView` in the `.idle, .welcome` case:

```swift
            case .idle, .welcome:
                WelcomeView(freeBallEngine: freeBallEngine)
                    .transition(.opacity)
```

**Step 2: Construct the engine in `AppDelegate`** (after the `eng` setup, before the panel):

```swift
        let freeBallEngine = FreeBallEngine(
            state: state,
            captureService: captureService,
            ocrService: ocrService,
            aiService: aiService
        )
        freeBallEngine.modelContext = container.mainContext
        self.freeBallEngine = freeBallEngine
```

Add the stored property near the top of `AppDelegate`:

```swift
    var freeBallEngine: FreeBallEngine?
```

**Step 3: Recover dangling sessions on launch** (a FreeBall session left open by a quit/crash). After loading drift limit, add:

```swift
        // A FreeBall session with no endedAt was interrupted (quit/crash). Close it
        // silently and mark it recap-pending so its raw captures aren't lost and it
        // doesn't look "live" forever.
        Task { @MainActor in
            let ctx = container.mainContext
            let dangling = (try? ctx.fetch(FetchDescriptor<FreeBallSession>())) ?? []
            for s in dangling where s.endedAt == nil {
                s.endedAt = .now
                s.recapPending = true
            }
            try? ctx.save()
        }
```

**Step 4: Pass `freeBallEngine` into the hosting view:**

```swift
        panel.contentView = NSHostingView(
            rootView: RootCoordinatorView(engine: eng, freeBallEngine: freeBallEngine, aiService: aiService)
                .environmentObject(state)
        )
```

**Step 5: Build + test**

Run: `make -C src build && make -C src test`
Expected: build OK; all suites PASS.

**Step 6: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): wire FreeBallEngine through AppDelegate + welcome button + crash recovery"
```

---

### Task 14: Manual smoke test

**Step 1: Build the app**

Run: `make -C src build`
Expected: build OK.

**Step 2: Run and verify the loop** (requires Ollama running with the model pulled, or `AI_PROVIDER`/`OPENROUTER_API_KEY` set):

Run: `make -C src run`

Verify, in order:
1. Welcome screen shows a **FreeBall** outline button below "Let's get started".
2. Click FreeBall → panel collapses to a calm ball at the right edge with a timer.
3. Click the ball → log card with the running timer + "End Session".
4. Click "back" → returns to the collapsed ball (session still running).
5. Do a couple of distinct activities (code editor, a browser) for ~30–60s.
6. Click the ball → End Session → brief "making sense…" → recap with narrative, category bars, and an insight note.
7. Click Done → back to welcome.
8. Run FreeBall a second time and confirm the insight can reference prior sessions.

**Step 3: Verify persistence** (optional): the SwiftData store retains `FreeBallSession` rows across launches; the second session's recap should draw on the first.

**Step 4: Final commit** (if any tweaks were needed during smoke):

```bash
cd src && git add -A && git commit -m "chore(freeball): manual smoke fixes"
```

---

## Notes & deferred items (per design)

- **No history browser** in v1 — cross-session learning surfaces only through the insight note. The data model supports a future trends view.
- **No "clear FreeBall history"** affordance yet (transcripts kept forever). Sensible fast-follow given the sensitivity of full screen text.
- **Chunk-then-stitch** for extreme sessions is deferred; v1 uses time-weighted truncation (`FreeBallCondenser`).
- **AI-down at launch**: the startup health probe still routes to `.aiUnavailable` (welcome, and thus FreeBall, is hidden until recovery). FreeBall itself only needs AI at End Session; decoupling its entry from the accountability provider probe is out of v1 scope.
- The dedup default threshold (0.85) and the budgeting constants live in `AppConstants` — tune during the manual run if blocks split too eagerly or never.
```
