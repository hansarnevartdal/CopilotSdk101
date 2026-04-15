// Example 4: Multiple custom tools on one session.
// Let the agent discover files, read them, and submit a markdown summary without hardcoded orchestration.

using System.ComponentModel;
using System.IO.Enumeration;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;

const int MaxSearchResults = 20;
const int MaxFileLines = 80;

var repoPath = FindRepoRoot(AppContext.BaseDirectory);
string? capturedSummary = null;
var toolNamesByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

var searchFilesTool = AIFunctionFactory.Create(
    ([Description("Wildcard pattern matched against repo-relative paths, for example '*.csproj', 'README.md', or '4-MultiToolAgent/*'.")]
     string pattern) =>
    {
        var normalizedPattern = NormalizePath(pattern);
        var matches = Directory
            .EnumerateFiles(repoPath, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                ReturnSpecialDirectories = false,
            })
            .Select(path => NormalizePath(Path.GetRelativePath(repoPath, path)))
            .Where(relativePath => !IsIgnoredPath(relativePath))
            .Where(relativePath => FileSystemName.MatchesSimpleExpression(normalizedPattern, relativePath, ignoreCase: true))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSearchResults + 1)
            .ToList();

        if (matches.Count == 0)
        {
            return $"No files matched `{pattern}`.";
        }

        var wasTrimmed = matches.Count > MaxSearchResults;
        var visibleMatches = wasTrimmed ? matches.Take(MaxSearchResults).ToList() : matches;
        var lines = string.Join(Environment.NewLine, visibleMatches.Select(path => $"- {path}"));
        var suffix = wasTrimmed ? $"{Environment.NewLine}- ... more matches omitted" : string.Empty;

        return $"Found {matches.Count - (wasTrimmed ? 1 : 0)} match(es) for `{pattern}`:{Environment.NewLine}{lines}{suffix}";
    },
    "search_files",
    "Search the repository for files whose relative path matches a wildcard pattern.");

var readFileTool = AIFunctionFactory.Create(
    ([Description("Repo-relative file path returned by search_files.")]
     string relativePath) =>
    {
        var normalizedRelativePath = NormalizePath(relativePath);
        var fullPath = Path.GetFullPath(Path.Combine(repoPath, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!fullPath.StartsWith(repoPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only files inside the configured repository can be read.");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The requested file does not exist.", normalizedRelativePath);
        }

        var lines = File.ReadAllLines(fullPath);
        var preview = lines
            .Take(MaxFileLines)
            .Select((line, index) => $"{index + 1}. {line}");

        var truncatedNotice = lines.Length > MaxFileLines
            ? $"{Environment.NewLine}... {lines.Length - MaxFileLines} more line(s) omitted."
            : string.Empty;

        return $"# {NormalizePath(Path.GetRelativePath(repoPath, fullPath))}{Environment.NewLine}{string.Join(Environment.NewLine, preview)}{truncatedNotice}";
    },
    "read_file",
    "Read a text file from the repository using a repo-relative path.");

var summarizeFindingsTool = AIFunctionFactory.Create(
    ([Description("A concise markdown summary of the findings based on the files you inspected.")]
     string markdownSummary) =>
    {
        capturedSummary = markdownSummary.Trim();
        return "Summary captured.";
    },
    "summarize_findings",
    "Save the final markdown summary after inspecting the repository.");

await using var client = new CopilotClient(new CopilotClientOptions
{
    Cwd = repoPath,
});

await using var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-5.4",
    ReasoningEffort = "low",
    Streaming = true,
    OnPermissionRequest = PermissionHandler.ApproveAll,
    Tools = [searchFilesTool, readFileTool, summarizeFindingsTool],
    SystemMessage = new SystemMessageConfig
    {
        Mode = SystemMessageMode.Append,
        Content = """
            You are demonstrating how a Copilot SDK session can orchestrate multiple custom tools.
            For repository exploration tasks, prefer this sequence when helpful:
            1. Use search_files to discover the relevant paths.
            2. Use read_file on the most relevant matches.
            3. When you finish, you MUST call summarize_findings with a concise markdown summary.
            Keep the summary practical and reference the files you inspected.
            """,
    },
});

Console.WriteLine("=== Copilot SDK — Multi-Tool Agent ===");
Console.WriteLine($"Target repo: {repoPath}");
Console.WriteLine("Loaded tools: search_files, read_file, summarize_findings");
Console.WriteLine("Press Enter to run the suggested prompt, type your own prompt, or type 'exit' to quit.\n");

var suggestedPrompt = "Summarize the examples in this repository and recommend which sample a beginner should start with first.";
Console.WriteLine($"Suggested prompt: {suggestedPrompt}\n");

using var subscription = session.On(evt =>
{
    switch (evt)
    {
        case AssistantMessageDeltaEvent delta:
            Console.Write(delta.Data.DeltaContent);
            break;
        case ToolExecutionStartEvent start:
            toolNamesByCallId[start.Data.ToolCallId] = start.Data.ToolName;
            Console.WriteLine();
            Console.WriteLine($"[tool:start] {start.Data.ToolName} {FormatArguments(start.Data.Arguments)}");
            break;
        case ToolExecutionProgressEvent progress when !string.IsNullOrWhiteSpace(progress.Data.ProgressMessage):
            Console.WriteLine($"[tool:progress] {LookupToolName(progress.Data.ToolCallId)}: {progress.Data.ProgressMessage}");
            break;
        case ToolExecutionCompleteEvent complete:
            Console.WriteLine($"[tool:{(complete.Data.Success ? "done" : "failed")}] {LookupToolName(complete.Data.ToolCallId)}");
            if (!complete.Data.Success && complete.Data.Error is not null)
            {
                Console.WriteLine($"  {complete.Data.Error}");
            }
            break;
        case SessionErrorEvent error:
            Console.WriteLine($"\n[ERROR] {error.Data}");
            break;
    }
});

while (true)
{
    Console.Write("> ");
    var prompt = Console.ReadLine();

    if (prompt?.Equals("exit", StringComparison.OrdinalIgnoreCase) == true)
    {
        break;
    }

    if (string.IsNullOrWhiteSpace(prompt))
    {
        prompt = suggestedPrompt;
    }

    capturedSummary = null;
    toolNamesByCallId.Clear();

    Console.WriteLine();

    await session.SendAndWaitAsync(
        new MessageOptions { Prompt = prompt },
        timeout: TimeSpan.FromMinutes(5));

    Console.WriteLine();

    if (!string.IsNullOrWhiteSpace(capturedSummary))
    {
        Console.WriteLine("╔══════════════════════════════╗");
        Console.WriteLine("║     Captured Summary         ║");
        Console.WriteLine("╚══════════════════════════════╝");
        Console.WriteLine(capturedSummary);
        Console.WriteLine();
    }
}

return;

string LookupToolName(string toolCallId) =>
    toolNamesByCallId.TryGetValue(toolCallId, out var toolName) ? toolName : toolCallId;

static string FormatArguments(object? arguments)
{
    if (arguments is null)
    {
        return string.Empty;
    }

    var text = arguments.ToString();
    return string.IsNullOrWhiteSpace(text) ? string.Empty : text;
}

static bool IsIgnoredPath(string relativePath) =>
    relativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
    || relativePath.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
    || relativePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase);

static string NormalizePath(string path) => path.Replace('\\', '/');

static string FindRepoRoot(string startDirectory)
{
    var current = new DirectoryInfo(startDirectory);

    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "CopilotSdk101.slnx")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate the repository root from the current application directory.");
}
