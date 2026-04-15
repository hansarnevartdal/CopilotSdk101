# Copilot SDK 101

Progressive examples showing how to use the [GitHub Copilot SDK](https://www.nuget.org/packages/GitHub.Copilot.SDK) from .NET to build agentic coding tools.

## Prerequisites

- .NET 10
- `GitHub.Copilot.SDK` 0.2.1 (restored automatically)
- A GitHub Copilot subscription

## Examples

### 1 — Repo Instructions

The simplest starting point. Point `CopilotClient` at a git repo and it automatically discovers `.github/copilot-instructions.md` and skills — no extra config needed.

```
cd 1-RepoInstructions
dotnet run
```

### 2 — Bring Your Own Instructions

Load custom skills and inject a custom system prompt programmatically. This example adds a security-review skill and OWASP-focused instructions without touching the target repo.

```
cd 2-BringYourOwnInstructions
dotnet run
```

### 3 — Structured Output

Use `AIFunctionFactory` to define a tool the agent *must* call, turning free-form LLM output into a strongly-typed C# object (`SecurityResult`). The agent analyzes a repo and reports findings through the tool.

```
cd 3-StructuredOutput
dotnet run
```

### 4 — Multi-Tool Agent

Register multiple custom tools on one session and let the agent decide how to chain them together. This example exposes `search_files`, `read_file`, and `summarize_findings`, then logs tool execution events so you can watch the agent discover files, inspect them, and submit a markdown summary.

```
cd 4-MultiToolAgent
dotnet run
```

### 5 — Conversational Agent with Session History

Reuse a single Copilot session across multiple turns so the agent remembers earlier context. This example runs a guided three-turn code-review conversation, streams every response, and then keeps the same session open for additional follow-up prompts.

```
cd 5-ConversationalAgent
dotnet run
```

## Key Concepts

| Concept | Where |
|---|---|
| Auto-discover repo instructions & skills | Example 1 |
| Custom `SkillDirectories` | Example 2 |
| Custom `SystemMessage` (append mode) | Example 2 |
| Streaming responses via events | Examples 1 & 2 |
| Structured output via `AIFunction` tools | Example 3 |
| `SendAndWaitAsync` (single-shot) | Example 3 |
| Multi-turn session reuse | Example 5 |
| Repeated `SendAsync` calls with context | Example 5 |
| Interactive follow-up prompts | Example 5 |
| Multiple custom tools in one session | Example 4 |
| Agent-selected tool chaining | Example 4 |
| Tool execution events for observability | Example 4 |
