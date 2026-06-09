# FreeBall Context-Aware Recap Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Extend FreeBall's End-Session recap to extract four context dimensions (Working on / People / Code / Open threads) on top of the existing narrative+breakdown+insight, persist them, and make them retrievable via an in-app history browser and Markdown export.

**Architecture:** Additive. `FreeBallSummary`/`FreeBallRecap`/`FreeBallSession` gain four `[String]` context fields; the single `summarizeFreeBall` call returns them in the same JSON. A new `.freeBallHistory` phase lists stored sessions and re-opens the existing `FreeBallRecapView` (now context-aware) for any one. Export renders a `FreeBallRecap` to Markdown and reveals it in Finder. No new AI calls, no second recap renderer.

**Tech Stack:** Swift, SwiftUI, NSPanel, SwiftData (macOS 14+), Ollama/OpenRouter via `AIService`. Custom MicroTest harness (`suite("X") { expect(bool, "desc") }`, files register in `src/Tests/TestRunner/main.swift`). Build: `make -C src build`. Test: `make -C src test`. All commits run in the inner `src/` repo.

**Design doc:** `planning/plans/2026-06-10-freeball-context-recap-design.md`

**Conventions (from the FreeBall v1 build):**
- Sync suites register inline in `runAllTests()`; `@MainActor async` engine suites are `await`ed at the bottom.
- Adding an `AppPhase` case breaks three exhaustive switches (compiler-enforced): `FloatingPanel.resize(for:)`, `RootCoordinatorView.body`, and `AppPhaseTests` (its no-`default` switch).
- SwiftData test helpers MUST keep the `ModelContainer` alive for the whole suite (a `ModelContext` does not retain its container — dropping it traps on insert).
- `parseFreeBallSummary` must default every missing field to empty (never crash on a model that omits one).

---

### Task 1: Context fields on the summary/recap types + parser

**Files:**
- Modify: `src/Sources/AccountaBall/Models/FreeBallRecap.swift`
- Modify: `src/Sources/AccountaBall/Util/AIPrompts.swift` (`parseFreeBallSummary`)
- Test: `src/Tests/AccountaBallTests/FreeBallSummarizeTests.swift`

**Step 1: Extend the failing test.** In `FreeBallSummarizeTests.swift`, replace the JSON in `FreeBallSummarize_parse` to include the new arrays and assert them:

```swift
let json = #"{"narrative":"You mostly coded.","categories":[{"label":"Coding","minutes":45}],"insight":"You code in long blocks.","workingOn":["refactoring the timeout handling"],"people":["Sarah (Slack) — launch Friday"],"codeContext":["OllamaAIService.swift"],"openThreads":["reply to Sarah"]}"#
let out = AIPrompts.parseFreeBallSummary(json)
expect(out.workingOn == ["refactoring the timeout handling"], "workingOn parsed")
expect(out.people.first == "Sarah (Slack) — launch Friday", "people parsed")
expect(out.codeContext == ["OllamaAIService.swift"], "codeContext parsed")
expect(out.openThreads == ["reply to Sarah"], "openThreads parsed")
```

In `FreeBallSummarize_parse_badJSON`, add: `expect(out.workingOn.isEmpty, "bad json -> empty workingOn")`.

**Step 2: Run to verify it fails**

Run: `make -C src test`
Expected: FAIL — `FreeBallSummary` has no `workingOn` (compile error).

**Step 3: Add the fields to the structs.** In `FreeBallRecap.swift`, extend `FreeBallSummary`:

```swift
struct FreeBallSummary: Equatable {
    let narrative: String
    let categories: [CategorySpan]
    let insight: String
    let workingOn: [String]
    let people: [String]
    let codeContext: [String]
    let openThreads: [String]
}
```

And `FreeBallRecap` (add the four lists + a `date` for export/history; keep existing fields):

```swift
struct FreeBallRecap: Equatable {
    let date: Date
    let duration: TimeInterval
    let narrative: String
    let categories: [CategorySpan]
    let insight: String
    let workingOn: [String]
    let people: [String]
    let codeContext: [String]
    let openThreads: [String]
    let recapPending: Bool
}
```

Also extend `FreeBallPastRecap` with `let openThreads: [String]` (feed-forward in Task 3).

**Step 4: Update `parseFreeBallSummary`.** Add a string-array helper and parse the four keys:

```swift
let strings: (String) -> [String] = { key in
    (obj[key] as? [String])?.compactMap { $0.isEmpty ? nil : $0 } ?? []
}
return FreeBallSummary(narrative: narrative, categories: cats, insight: insight,
                       workingOn: strings("workingOn"), people: strings("people"),
                       codeContext: strings("codeContext"), openThreads: strings("openThreads"))
```

Update the early `return FreeBallSummary(...)` failure path to pass `workingOn: [], people: [], codeContext: [], openThreads: []`.

**Step 5: Fix all other `FreeBallSummary(` / `FreeBallRecap(` call sites** the compiler now flags (engine, fakes, Claude stub). Use empty arrays for the new fields where a stub. (Engine is rewired in Task 4; for now make it compile by passing the summary's fields through / empty.)

**Step 6: Run tests**

Run: `make -C src test`
Expected: PASS.

**Step 7: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): add 4 context fields to summary/recap types + parser"
```

---

### Task 2: Persist the context fields on FreeBallSession

**Files:**
- Modify: `src/Sources/AccountaBall/Models/FreeBallModels.swift`
- Test: `src/Tests/AccountaBallTests/FreeBallPersistenceTests.swift`

**Step 1: Extend the failing test.** In `FreeBallPersistenceTests.swift`, after setting `session.insight`, add:

```swift
session.workingOn = ["refactor timeout"]
session.people = ["Sarah (Slack)"]
session.codeContext = ["OllamaAIService.swift"]
session.openThreads = ["reply to Sarah"]
```

After fetch, assert:

```swift
expect(fetched.first?.workingOn == ["refactor timeout"], "workingOn round-trip")
expect(fetched.first?.openThreads == ["reply to Sarah"], "openThreads round-trip")
```

**Step 2: Run to verify it fails**

Run: `make -C src test`
Expected: FAIL — `FreeBallSession` has no `workingOn`.

**Step 3: Add the stored properties** to `FreeBallSession` (beside `var insight`):

```swift
var workingOn: [String] = []
var people: [String] = []
var codeContext: [String] = []
var openThreads: [String] = []
```

**Step 4: Run tests**

Run: `make -C src test`
Expected: PASS.

**Step 5: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): persist context fields on FreeBallSession"
```

---

### Task 3: Rewrite the prompt + feed openThreads forward + bigger budget

**Files:**
- Modify: `src/Sources/AccountaBall/Util/AIPrompts.swift` (`freeBallSystem`, `buildFreeBallPrompt`)
- Modify: `src/Sources/AccountaBall/Util/AppConstants.swift` (`freeBallMaxTranscriptChars`)
- Test: `src/Tests/AccountaBallTests/FreeBallSummarizeTests.swift`

**Step 1: Extend the `FreeBallSummarize_prompt` test** to cover feed-forward:

```swift
let past = [FreeBallPastRecap(narrative: "Lots of email.", categories: [CategorySpan(label: "Email", minutes: 30)], insight: "Mornings are emaily.", openThreads: ["reply to vendor"])]
let prompt = AIPrompts.buildFreeBallPrompt(transcript: transcript, pastRecaps: past)
expect(prompt.contains("editing main.swift"), "transcript text included")
expect(prompt.contains("reply to vendor"), "past open threads fed forward")
```

**Step 2: Run to verify it fails**

Run: `make -C src test`
Expected: FAIL — `FreeBallPastRecap` has no `openThreads` arg / prompt lacks the thread.

**Step 3: Rewrite `freeBallSystem`** to request the four lists, grounded:

```swift
static let freeBallSystem = """
You are observing how a user spends a work session. You are NOT judging whether
they stayed on task — there is no declared task. Read the transcript of what was
on their screen (each block notes roughly how long that screen was up) and any
summaries of PAST sessions, then report where their time went AND the substance
of the work. Ground EVERY item in the transcript — only list a person, file, or
thread that actually appears on screen; never invent. If a list has nothing,
return an empty array. Keep items short and concrete.
Respond with JSON only:
{"narrative":"<2-4 sentences, what they spent the session on>",
 "categories":[{"label":"<activity>","minutes":<int>}, ...],
 "insight":"<one habit observation; may reference past sessions>",
 "workingOn":["<active task/project, e.g. 'refactoring the timeout handling'>"],
 "people":["<who + gist, e.g. 'Sarah (Slack) — wants launch Friday'>"],
 "codeContext":["<file/function/error touched, e.g. 'OllamaAIService.swift — URLError timeout'>"],
 "openThreads":["<unfinished / awaiting-reply / undecided item>"]}
Output JSON only, no prose.
"""
```

**Step 4: Feed past openThreads forward** in `buildFreeBallPrompt`. In the `pastBlock` map, append threads when present:

```swift
let threads = r.openThreads.isEmpty ? "" : " Open: \(r.openThreads.joined(separator: "; "))"
return "Session \(i + 1): \(r.narrative) [\(cats)] Insight: \(r.insight)\(threads)"
```

**Step 5: Bump the transcript budget** in `AppConstants.swift`:

```swift
static let freeBallMaxTranscriptChars: Int = 28000
```

**Step 6: Run tests**

Run: `make -C src test`
Expected: PASS.

**Step 7: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): context-extraction prompt + feed open threads forward + bigger budget"
```

---

### Task 4: Wire context through the engine + provider schemas

**Files:**
- Modify: `src/Sources/AccountaBall/Engine/FreeBallEngine.swift` (`end`, `publishRecap`, `fetchPastRecaps`, trivial-session path)
- Modify: `src/Sources/AccountaBall/Services/OllamaAIService.swift` (`summarizeFreeBall` JSON schema)
- Test: `src/Tests/AccountaBallTests/FreeBallEngineTests.swift`

**Step 1: Strengthen the engine test.** Give `FreeBallFakeAI.summary` context and assert it lands on state + session:

```swift
var summary = FreeBallSummary(narrative: "N", categories: [CategorySpan(label: "Coding", minutes: 5)], insight: "I",
    workingOn: ["W"], people: ["P"], codeContext: ["C"], openThreads: ["O"])
```

In `FreeBallEngine_end_summarizes`, add:

```swift
expect(state.freeBallRecap?.workingOn == ["W"], "workingOn published to recap")
expect(state.freeBallRecap?.openThreads == ["O"], "openThreads published to recap")
```

**Step 2: Run to verify it fails**

Run: `make -C src test`
Expected: FAIL — recap has no `workingOn` value (engine drops it).

**Step 3: Store + publish context in `end()`.** Where the summary is applied to the session, add:

```swift
session.workingOn = summary.workingOn
session.people = summary.people
session.codeContext = summary.codeContext
session.openThreads = summary.openThreads
```

**Step 4: Update `publishRecap`** to carry the new fields + date. Change its signature to take the session date (or pass `now()`), and build:

```swift
private func publishRecap(date: Date, duration: TimeInterval, summary: FreeBallSummary, pending: Bool) {
    state.freeBallRecap = FreeBallRecap(
        date: date, duration: duration, narrative: summary.narrative,
        categories: summary.categories, insight: summary.insight,
        workingOn: summary.workingOn, people: summary.people,
        codeContext: summary.codeContext, openThreads: summary.openThreads,
        recapPending: pending)
}
```

Update both `publishRecap` call sites (AI-success and AI-fail) and the trivial-session path to pass `date: session.startedAt` and a summary with empty context arrays where applicable. The trivial + failure summaries use `FreeBallSummary(narrative:..., categories: [], insight: "", workingOn: [], people: [], codeContext: [], openThreads: [])`.

**Step 5: Feed openThreads in `fetchPastRecaps`.** In the `.map`, add `openThreads: $0.openThreads` to the `FreeBallPastRecap(...)`.

**Step 6: Add the new fields to the Ollama JSON schema** in `summarizeFreeBall`:

```swift
"workingOn": ["type": "array", "items": ["type": "string"]],
"people": ["type": "array", "items": ["type": "string"]],
"codeContext": ["type": "array", "items": ["type": "string"]],
"openThreads": ["type": "array", "items": ["type": "string"]],
```

and add them to the `"required"` list.

**Step 7: Run tests**

Run: `make -C src test`
Expected: PASS.

**Step 8: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): store + publish context through engine; Ollama schema"
```

---

### Task 5: FreeBallRecap(from: FreeBallSession) initializer

**Files:**
- Modify: `src/Sources/AccountaBall/Models/FreeBallRecap.swift`
- Test: `src/Tests/AccountaBallTests/FreeBallSummarizeTests.swift` (new suite) or a new `FreeBallRecapFromSessionTests.swift` (register it)

**Step 1: Write the failing test** (new suite). Build an in-memory session, set fields, init a recap from it:

```swift
suite("FreeBallRecap_fromSession") {
    let s = FreeBallSession(startedAt: Date(timeIntervalSince1970: 1000))
    s.endedAt = Date(timeIntervalSince1970: 1600)   // 600s
    s.narrative = "did stuff"; s.workingOn = ["W"]; s.openThreads = ["O"]
    let r = FreeBallRecap(from: s)
    expect(r.duration == 600, "duration from start/end")
    expect(r.narrative == "did stuff", "narrative copied")
    expect(r.workingOn == ["W"], "workingOn copied")
    expect(r.recapPending == false, "pending copied")
}
```

Guard with `if #available(macOS 14, *)` if the runner requires it (mirror existing FreeBall tests).

**Step 2: Run to verify it fails** — `make -C src test` → FAIL (no such initializer).

**Step 3: Add the initializer** in `FreeBallRecap.swift`:

```swift
@available(macOS 14, *)
extension FreeBallRecap {
    init(from s: FreeBallSession) {
        let end = s.endedAt ?? s.startedAt
        self.init(date: end, duration: end.timeIntervalSince(s.startedAt),
                  narrative: s.narrative, categories: s.categories, insight: s.insight,
                  workingOn: s.workingOn, people: s.people, codeContext: s.codeContext,
                  openThreads: s.openThreads, recapPending: s.recapPending)
    }
}
```

**Step 4: Run tests** — PASS.

**Step 5: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): FreeBallRecap(from: FreeBallSession)"
```

---

### Task 6: FreeBallMarkdown.render (pure)

**Files:**
- Create: `src/Sources/AccountaBall/Util/FreeBallMarkdown.swift`
- Test: `src/Tests/AccountaBallTests/FreeBallMarkdownTests.swift` (register in runner)

**Step 1: Write the failing test:**

```swift
func runFreeBallMarkdownTests() {
    suite("FreeBallMarkdown") {
        let r = FreeBallRecap(date: Date(timeIntervalSince1970: 0), duration: 600,
            narrative: "Mostly coded.", categories: [CategorySpan(label: "Coding", minutes: 10)],
            insight: "Long blocks.", workingOn: ["refactor timeout"], people: ["Sarah (Slack)"],
            codeContext: ["OllamaAIService.swift"], openThreads: ["reply to Sarah"], recapPending: false)
        let md = FreeBallMarkdown.render(recap: r)
        expect(md.contains("# FreeBall"), "has a title")
        expect(md.contains("Mostly coded."), "narrative present")
        expect(md.contains("## Working on"), "working on section")
        expect(md.contains("refactor timeout"), "working on item")
        expect(md.contains("## Open threads"), "open threads section")
        expect(md.contains("reply to Sarah"), "thread item")

        let empty = FreeBallRecap(date: Date(timeIntervalSince1970: 0), duration: 0, narrative: "",
            categories: [], insight: "", workingOn: [], people: [], codeContext: [], openThreads: [], recapPending: false)
        expect(!FreeBallMarkdown.render(recap: empty).contains("## Working on"), "empty section omitted")
    }
}
```

Register `runFreeBallMarkdownTests()` in the runner sync block.

**Step 2: Run to verify it fails** — FAIL (undefined).

**Step 3: Implement** `FreeBallMarkdown.swift`:

```swift
import Foundation

/// Render a FreeBallRecap to a self-contained Markdown document. Sections with no
/// items are omitted. Pure + unit-tested; used by the export action.
enum FreeBallMarkdown {
    static func render(recap r: FreeBallRecap) -> String {
        let df = DateFormatter(); df.dateStyle = .medium; df.timeStyle = .short
        let mins = Int((r.duration / 60).rounded())
        var out = "# FreeBall — \(df.string(from: r.date))\n\n_\(mins)m session_\n"
        if !r.narrative.isEmpty { out += "\n\(r.narrative)\n" }
        if !r.categories.isEmpty {
            out += "\n## Time\n"
            for c in r.categories.sorted(by: { $0.minutes > $1.minutes }) { out += "- \(c.label): \(c.minutes)m\n" }
        }
        func section(_ title: String, _ items: [String]) {
            guard !items.isEmpty else { return }
            out += "\n## \(title)\n"
            for i in items { out += "- \(i)\n" }
        }
        section("Working on", r.workingOn)
        section("People & conversations", r.people)
        section("Code context", r.codeContext)
        section("Open threads", r.openThreads)
        if !r.insight.isEmpty { out += "\n## Insight\n\(r.insight)\n" }
        return out
    }
}
```

**Step 4: Run tests** — PASS.

**Step 5: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): FreeBallMarkdown.render pure function"
```

---

### Task 7: Export action (write file + reveal in Finder)

**Files:**
- Create: `src/Sources/AccountaBall/Util/FreeBallExport.swift`
- Test: `src/Tests/AccountaBallTests/FreeBallExportTests.swift` (register; test the filename/path builder only — file IO + Finder reveal are not unit-tested)

**Step 1: Write the failing test** for the pure filename builder:

```swift
func runFreeBallExportTests() {
    suite("FreeBallExport_filename") {
        let name = FreeBallExport.filename(for: Date(timeIntervalSince1970: 0))
        expect(name.hasSuffix(".md"), "is a markdown file")
        expect(name.hasPrefix("freeball-"), "prefixed")
    }
}
```

**Step 2: Run to verify it fails** — FAIL (undefined).

**Step 3: Implement** `FreeBallExport.swift`:

```swift
import Foundation
import AppKit

/// Write a FreeBall recap to ~/Documents/AccountaBall/FreeBall/<file>.md and
/// reveal it in Finder. The folder becomes a browsable, grep-able archive.
enum FreeBallExport {
    static func filename(for date: Date) -> String {
        let df = DateFormatter(); df.dateFormat = "yyyy-MM-dd-HHmm"
        return "freeball-\(df.string(from: date)).md"
    }

    /// Returns the written file URL, or nil on failure.
    @discardableResult
    static func export(_ recap: FreeBallRecap) -> URL? {
        let fm = FileManager.default
        guard let docs = fm.urls(for: .documentDirectory, in: .userDomainMask).first else { return nil }
        let dir = docs.appendingPathComponent("AccountaBall/FreeBall", isDirectory: true)
        try? fm.createDirectory(at: dir, withIntermediateDirectories: true)
        let url = dir.appendingPathComponent(filename(for: recap.date))
        do {
            try FreeBallMarkdown.render(recap: recap).write(to: url, atomically: true, encoding: .utf8)
            NSWorkspace.shared.activateFileViewerSelecting([url])
            return url
        } catch { return nil }
    }
}
```

**Step 4: Run tests** — PASS.

**Step 5: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): Markdown export to Documents + reveal in Finder"
```

---

### Task 8: AppPhase .freeBallHistory + exhaustive switches

**Files:**
- Modify: `src/Sources/AccountaBall/Models/AppPhase.swift`
- Modify: `src/Sources/AccountaBall/Views/FloatingPanel.swift` (`resize(for:)`)
- Modify: `src/Sources/AccountaBall/Views/RootCoordinatorView.swift` (placeholder branch for now)
- Test: `src/Tests/AccountaBallTests/AppPhaseTests.swift`

**Step 1: Add a failing assertion** in `AppPhaseTests.swift` (new suite):

```swift
suite("AppPhase_freeBallHistory") {
    let p: AppPhase = .freeBallHistory
    switch p { case .freeBallHistory: expect(true, "covered"); default: expect(true, "other") }
}
```

Also add `case .freeBallHistory: return "freeBallHistory"` to the existing no-`default` exhaustive switch.

**Step 2: Run to verify it fails** — FAIL (no such case).

**Step 3: Add the case** in `AppPhase.swift` after `case freeBallRecap`:

```swift
    case freeBallHistory  // past-sessions browser
```

**Step 4: Panel sizing** in `FloatingPanel.resize(for:)`:

```swift
        case .freeBallHistory:
            setContentSize(clampedToScreen(NSSize(width: 460, height: 560))); center()
```

**Step 5: Placeholder branch** in `RootCoordinatorView.body` (real view in Task 9):

```swift
            case .freeBallHistory:
                Color.black.ignoresSafeArea().transition(.opacity)
```

**Step 6: Run tests** — `make -C src test` → PASS.

**Step 7: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): add .freeBallHistory phase + panel sizing"
```

---

### Task 9: Render context sections in the recap + history browser

**Files:**
- Modify: `src/Sources/AccountaBall/Views/FreeBallView.swift` (add context sections to `FreeBallRecapView`; add Export button)
- Create: `src/Sources/AccountaBall/Views/FreeBallHistoryView.swift`
- Modify: `src/Sources/AccountaBall/Views/RootCoordinatorView.swift` (real `.freeBallHistory` branch)

> UI — verified by build + the manual smoke (Task 11).

**Step 1: Add context sections to `FreeBallRecapView.content(_:)`** after the insight block, before the Done button. Add a reusable section builder and render the four lists when non-empty:

```swift
    @ViewBuilder private func contextSection(_ title: String, _ items: [String]) -> some View {
        if !items.isEmpty {
            VStack(alignment: .leading, spacing: 4) {
                Text(title).font(.caption).foregroundStyle(.white.opacity(0.5))
                ForEach(items, id: \.self) { Text("• \($0)").font(.system(size: 13)).foregroundStyle(.white.opacity(0.9))
                    .fixedSize(horizontal: false, vertical: true) }
            }
        }
    }
```

Call within the non-pending branch:

```swift
contextSection("Working on", recap.workingOn)
contextSection("People & conversations", recap.people)
contextSection("Code context", recap.codeContext)
contextSection("Open threads / next steps", recap.openThreads)
```

**Step 2: Add an Export button** beside Done in the recap:

```swift
Button("Export") { FreeBallExport.export(recap) }
    .buttonStyle(SecondaryButtonStyle())
```

**Step 3: Create `FreeBallHistoryView.swift`:**

```swift
import SwiftUI
import SwiftData

/// Browse past FreeBall sessions. Tap a row to re-open its full recap; export
/// any row to Markdown. Reads straight from the SwiftData store.
struct FreeBallHistoryView: View {
    @EnvironmentObject var state: AppState
    var modelContext: ModelContext
    var onOpen: (FreeBallRecap) -> Void
    var onBack: () -> Void
    @State private var sessions: [FreeBallSession] = []

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text("Past sessions").font(.system(size: 18, weight: .bold)).foregroundStyle(.white)
                Spacer()
                Button("back", action: onBack).font(.caption).foregroundStyle(.white.opacity(0.6)).buttonStyle(.plain)
            }
            if sessions.isEmpty {
                Text("No FreeBall sessions yet.").font(.system(size: 13)).foregroundStyle(.white.opacity(0.5))
            }
            ScrollView {
                VStack(spacing: 8) {
                    ForEach(sessions, id: \.id) { s in row(s) }
                }
            }
        }
        .padding(24).frame(maxWidth: .infinity, maxHeight: .infinity).background(Color.black)
        .onAppear(perform: load)
    }

    @ViewBuilder private func row(_ s: FreeBallSession) -> some View {
        let recap = FreeBallRecap(from: s)
        Button { onOpen(recap) } label: {
            HStack(alignment: .top) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(dateLabel(s)).font(.system(size: 12, weight: .semibold)).foregroundStyle(.white)
                    Text(s.narrative.isEmpty ? "(not summarized)" : s.narrative)
                        .font(.system(size: 12)).foregroundStyle(.white.opacity(0.6)).lineLimit(2)
                }
                Spacer()
                if s.recapPending { Circle().fill(Color.orange).frame(width: 6, height: 6) }
                Button { FreeBallExport.export(recap) } label: { Image(systemName: "square.and.arrow.down") }
                    .buttonStyle(.plain).foregroundStyle(.orange)
            }
            .padding(12).background(Color.white.opacity(0.05)).clipShape(RoundedRectangle(cornerRadius: 8))
        }.buttonStyle(.plain)
    }

    private func dateLabel(_ s: FreeBallSession) -> String {
        let df = DateFormatter(); df.dateStyle = .medium; df.timeStyle = .short
        let mins = Int(((s.endedAt ?? s.startedAt).timeIntervalSince(s.startedAt) / 60).rounded())
        return "\(df.string(from: s.startedAt)) · \(mins)m"
    }

    private func load() {
        let all = (try? modelContext.fetch(FetchDescriptor<FreeBallSession>())) ?? []
        sessions = all.filter { $0.endedAt != nil }.sorted { ($0.endedAt ?? .distantPast) > ($1.endedAt ?? .distantPast) }
    }
}
```

**Step 4: Wire the real branch** in `RootCoordinatorView` (replace the placeholder). It needs the model context — pass `freeBallEngine.modelContext` (add a public accessor) or thread the container. Simplest: expose `var modelContext: ModelContext?` on `FreeBallEngine` (already a `var`) and guard:

```swift
            case .freeBallHistory:
                if let ctx = freeBallEngine.modelContext {
                    FreeBallHistoryView(modelContext: ctx,
                        onOpen: { recap in
                            state.freeBallRecap = recap
                            state.freeBallViewingHistory = true
                            withAnimation { state.appPhase = .freeBallRecap }
                        },
                        onBack: { withAnimation { state.appPhase = .welcome } })
                    .transition(.opacity)
                } else { Color.black.ignoresSafeArea() }
```

Add `@Published var freeBallViewingHistory: Bool = false` to `AppState` (Task 10 uses it for Done routing).

**Step 5: Build**

Run: `make -C src build`
Expected: compiles (after Task 10 wires Done routing / welcome link, build once at the end of Task 10 if needed).

**Step 6: Commit** (may combine with Task 10)

```bash
cd src && git add -A && git commit -m "feat(freeball): context sections in recap + history browser + export buttons"
```

---

### Task 10: Welcome "Past sessions" link + Done routing

**Files:**
- Modify: `src/Sources/AccountaBall/Models/AppState.swift` (`freeBallViewingHistory`)
- Modify: `src/Sources/AccountaBall/Views/WelcomeView.swift` (Past sessions link)
- Modify: `src/Sources/AccountaBall/Views/RootCoordinatorView.swift` (recap `onDone` routing)
- Test: `src/Tests/AccountaBallTests/AppStateV2Tests.swift`

**Step 1: Failing test** for the new flag default (append to the `AppState_freeBall` suite):

```swift
expect(s.freeBallViewingHistory == false, "not viewing history by default")
```

**Step 2: Run to verify it fails** — FAIL (no such member). (If added in Task 9, this passes — keep the assertion as a guard.)

**Step 3: Add the flag** (if not already): `@Published var freeBallViewingHistory: Bool = false`.

**Step 4: Past-sessions link** in `WelcomeView`, under the FreeBall button:

```swift
                        Button("Past sessions") {
                            withAnimation { state.appPhase = .freeBallHistory }
                        }
                        .font(.caption).foregroundStyle(.white.opacity(0.5)).buttonStyle(.plain)
```

**Step 5: Route the recap's Done** in `RootCoordinatorView` `.freeBallRecap` case:

```swift
            case .freeBallRecap:
                FreeBallRecapView(onDone: {
                    if state.freeBallViewingHistory {
                        state.freeBallViewingHistory = false
                        withAnimation { state.appPhase = .freeBallHistory }
                    } else {
                        state.loadTasks()
                        withAnimation { state.appPhase = .welcome }
                    }
                })
                .transition(.opacity)
```

**Step 6: Build + test**

Run: `make -C src build && make -C src test`
Expected: build OK; all suites PASS.

**Step 7: Commit**

```bash
cd src && git add -A && git commit -m "feat(freeball): past-sessions link + history-aware recap Done routing"
```

---

### Task 11: Manual smoke test

Run: `make -C src run` (Ollama running, model pulled).

1. Run a FreeBall session with varied activity (code editor, a chat/email, a browser) for ~1–2 min.
2. End Session → recap now shows **Working on / People / Code / Open threads** sections (grounded, not invented) under the narrative + breakdown.
3. Click **Export** → a Finder window reveals `~/Documents/AccountaBall/FreeBall/freeball-….md`; open it and confirm the sections render.
4. Done → Welcome. Click **Past sessions** → the session appears; tap it → the full recap re-opens; **back**/Done returns to the history list, then Welcome.
5. Run a second session → confirm the insight / context can reference the prior session's open threads.

Tune if needed: `freeBallMaxTranscriptChars` (context too thin → raise), prompt grounding (invented items → tighten), `OLLAMA_MODEL` (garbled names → try a 14B).

Then update `docs/HOW-IT-WORKS.md` for the new recap sections + history/export.

---

## Notes / out of scope
- Ollama-first; OpenRouter inherits the prompt change, not separately QA'd. (OpenRouter's `summarizeFreeBall` uses `responseFormatJSON` so no schema edit needed, but verify its `maxTokens` is enough — bump to ~900 if context is truncated.)
- No history search/filter, bulk export, or in-UI delete (fast-follow).
- Clear-history affordance still deferred (store kept forever).
