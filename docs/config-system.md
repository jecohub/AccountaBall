# AccountaBall — Configuration System

AccountaBall doesn't use a dedicated config file or central configuration type. Configuration is spread across **4 layers**:

## 1. Environment Variables (runtime, read at startup)

All read from `ProcessInfo.processInfo.environment` in `AppDelegate.swift:18-43`:

| Variable | Default | Used By | Purpose |
|---|---|---|---|
| `AI_PROVIDER` | `ollama` | AI provider factory | Selects `ollama` or `openrouter` |
| `OLLAMA_HOST` | `http://localhost:11434` | `OllamaAIService` | Ollama daemon URL |
| `OLLAMA_MODEL` | `qwen2.5:7b` | `OllamaAIService` | Model for local inference |
| `OPENROUTER_API_KEY` | *(required)* | `OpenRouterAIService` | Cloud API key |
| `OPENROUTER_MODEL` | `anthropic/claude-haiku-4-5` | `OpenRouterAIService` | Cloud model ID |

No auto-fallback — a misconfigured provider shows a `setupHint` via the UI instead.

## 2. UserDefaults (task persistence)

- **Key:** `accountaball.tasks.v2`
- **Usage:** `AppState.saveTasks()`, `loadTasks()`, `clearSavedTasks()`
- Stores active session task list as JSON-encoded `[TaskItem]`

## 3. SwiftData (`@Model` container)

- Initialized in `AppDelegate.swift:47-53` via `AccountaBallStore.makeContainer()`
- On-disk store: `~/Library/Application Support/`
- 6 models: `WorkSession`, `TimelineEntry`, `JustificationEvent`, `KnowledgeTask`, `Allowance`, `TaskCompletion`
- In-memory option for tests: `makeContainer(inMemory: true)`

## 4. Debug Logging

- Path: `~/Library/Application Support/AccountaBall/logs/accountaball.log`
- 5 MB rotation to `.log.1`, writes to both stderr and log file

## Build Config

- **Makefile:** custom `swiftc` build, target `arm64-apple-macosx14.0`
- **Package.swift:** Swift 6 tools, macOS .v14, 3 targets
- No Info.plist or entitlements in source tree

## Key finding

**No dedicated config file or config struct exists in the project.** The word "configuration" in Swift source only refers to `URLSessionConfiguration`, `SCStreamConfiguration`, `ModelConfiguration`, and ButtonStyle's `Configuration` — none of which are app-level config.

Implications for future work:
- Adding a settings UI would require building a config layer from scratch
- Env var names are only documented in CLAUDE.md and design docs — no validation/parsing layer exists
- SwiftData store location is the default Application Support path (not user-configurable)
