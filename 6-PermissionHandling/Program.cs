// Example 6: Custom permission handling with explicit user approval before tool execution.
// The sample uses a temporary workspace so you can safely approve or reject file operations.

using System.Text;
using GitHub.Copilot.SDK;

const int MaxPreviewLines = 24;

var demoWorkspacePath = PrepareDemoWorkspace();
var demoFilePath = Path.Combine(demoWorkspacePath, "DemoGreeter.cs");
var permissionPolicy = new PermissionPolicy(demoWorkspacePath, demoFilePath);

await using var client = new CopilotClient(new CopilotClientOptions
{
    Cwd = demoWorkspacePath,
});

await using var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-5.4",
    ReasoningEffort = "low",
    Streaming = true,
    OnPermissionRequest = (request, _) => Task.FromResult(HandlePermission(request, permissionPolicy)),
    SystemMessage = new SystemMessageConfig
    {
        Mode = SystemMessageMode.Append,
        Content = """
            You are a cautious refactoring assistant.
            Work only inside the current workspace and prefer file reads and edits over shell commands.
            If a permission request is denied, explain what was blocked and continue with the safest useful response.
            Keep your explanations concise and mention the file you changed when an edit succeeds.
            """,
    },
});

using var subscription = session.On(evt =>
{
    switch (evt)
    {
        case AssistantMessageDeltaEvent delta:
            Console.Write(delta.Data.DeltaContent);
            break;
        case SessionErrorEvent error:
            Console.WriteLine($"\n[ERROR] {error.Data}");
            break;
    }
});

var suggestedPrompt = "Refactor DemoGreeter.cs so the greeting logic is easier to extend, keep the behavior the same, and explain the change after editing the file.";

Console.WriteLine("=== Copilot SDK — Permission Handling and Human-in-the-Loop ===");
Console.WriteLine($"Demo workspace: {demoWorkspacePath}");
Console.WriteLine($"Target file: {demoFilePath}");
Console.WriteLine("This sample replaces PermissionHandler.ApproveAll with a custom permission callback.");
Console.WriteLine("Shell commands are denied by rule. Read and write requests pause for your decision.");
Console.WriteLine("Permission choices:");
Console.WriteLine("  a = approve this request once");
Console.WriteLine("  d = deny this request once");
Console.WriteLine("  r = approve this read and future reads inside the demo workspace");
Console.WriteLine("  w = approve this write and future writes to DemoGreeter.cs");
Console.WriteLine();
Console.WriteLine("Tip: deny the first write once to see how the agent reacts, then rerun the prompt and approve it.");
Console.WriteLine($"Suggested prompt: {suggestedPrompt}");
Console.WriteLine("Press Enter to run the suggested prompt, type your own prompt, or type 'exit' to quit.\n");

ShowDemoFileSnapshot(demoFilePath);

while (true)
{
    Console.Write("> ");
    var prompt = Console.ReadLine();

    if (string.Equals(prompt, "exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    if (string.IsNullOrWhiteSpace(prompt))
    {
        prompt = suggestedPrompt;
    }

    Console.WriteLine();

    await session.SendAndWaitAsync(
        new MessageOptions { Prompt = prompt },
        timeout: TimeSpan.FromMinutes(5));

    Console.WriteLine();
    ShowDemoFileSnapshot(demoFilePath);
}

return;

static PermissionRequestResult HandlePermission(PermissionRequest request, PermissionPolicy permissionPolicy)
{
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(permissionPolicy);

    if (request is PermissionRequestShell shell)
    {
        Console.WriteLine();
        Console.WriteLine("[permission] Denied shell command by rule.");
        Console.WriteLine($"Command: {shell.FullCommandText}");

        return new PermissionRequestResult
        {
            Kind = PermissionRequestResultKind.DeniedByRules,
        };
    }

    if (request is PermissionRequestRead read && permissionPolicy.ShouldAutoApproveRead(read.Path))
    {
        Console.WriteLine($"\n[permission] Auto-approved read: {read.Path}");

        return new PermissionRequestResult
        {
            Kind = PermissionRequestResultKind.Approved,
        };
    }

    if (request is PermissionRequestWrite write && permissionPolicy.ShouldAutoApproveWrite(write.FileName))
    {
        Console.WriteLine($"\n[permission] Auto-approved write: {write.FileName}");

        return new PermissionRequestResult
        {
            Kind = PermissionRequestResultKind.Approved,
        };
    }

    while (true)
    {
        Console.WriteLine();
        Console.WriteLine("=== Permission request ===");
        Console.WriteLine(DescribeRequest(request));
        Console.WriteLine("Choose: [a] approve once  [d] deny once");

        if (request is PermissionRequestRead readRequest && permissionPolicy.CanConditionallyApproveRead(readRequest.Path))
        {
            Console.WriteLine("        [r] approve this read and future reads inside the demo workspace");
        }

        if (request is PermissionRequestWrite writeRequest && permissionPolicy.CanConditionallyApproveWrite(writeRequest.FileName))
        {
            Console.WriteLine("        [w] approve this write and future writes to DemoGreeter.cs");
        }

        Console.Write("> ");
        var input = Console.ReadLine()?.Trim().ToLowerInvariant();

        switch (input)
        {
            case "a":
                return new PermissionRequestResult
                {
                    Kind = PermissionRequestResultKind.Approved,
                };
            case "d":
                return new PermissionRequestResult
                {
                    Kind = PermissionRequestResultKind.DeniedInteractivelyByUser,
                };
            case "r" when request is PermissionRequestRead conditionalRead
                           && permissionPolicy.CanConditionallyApproveRead(conditionalRead.Path):
                permissionPolicy.EnableReadAutoApproval();
                Console.WriteLine("[permission] Future reads in the demo workspace will be auto-approved.");

                return new PermissionRequestResult
                {
                    Kind = PermissionRequestResultKind.Approved,
                };
            case "w" when request is PermissionRequestWrite conditionalWrite
                           && permissionPolicy.CanConditionallyApproveWrite(conditionalWrite.FileName):
                permissionPolicy.EnableTargetWriteAutoApproval();
                Console.WriteLine("[permission] Future writes to DemoGreeter.cs will be auto-approved.");

                return new PermissionRequestResult
                {
                    Kind = PermissionRequestResultKind.Approved,
                };
            default:
                Console.WriteLine("Please enter one of the listed permission choices.");
                break;
        }
    }
}

static string DescribeRequest(PermissionRequest request)
{
    ArgumentNullException.ThrowIfNull(request);

    var builder = new StringBuilder();

    switch (request)
    {
        case PermissionRequestRead read:
            builder.AppendLine("Type: read");
            builder.AppendLine($"Path: {read.Path}");
            if (!string.IsNullOrWhiteSpace(read.Intention))
            {
                builder.AppendLine($"Reason: {read.Intention}");
            }

            break;
        case PermissionRequestWrite write:
            builder.AppendLine("Type: write");
            builder.AppendLine($"Path: {write.FileName}");
            if (!string.IsNullOrWhiteSpace(write.Intention))
            {
                builder.AppendLine($"Reason: {write.Intention}");
            }

            var preview = BuildPreview(write.Diff, write.NewFileContents);
            if (!string.IsNullOrWhiteSpace(preview))
            {
                builder.AppendLine("Preview:");
                builder.Append(preview);
            }

            break;
        case PermissionRequestCustomTool customTool:
            builder.AppendLine("Type: custom_tool");
            builder.AppendLine($"Tool: {customTool.ToolName}");
            builder.AppendLine($"Description: {customTool.ToolDescription}");
            break;
        case PermissionRequestMcp mcp:
            builder.AppendLine("Type: mcp");
            builder.AppendLine($"Tool: {mcp.ToolTitle ?? mcp.ToolName}");
            builder.AppendLine($"Server: {mcp.ServerName}");
            break;
        default:
            builder.AppendLine($"Type: {request.Kind}");
            break;
    }

    return builder.ToString().TrimEnd();
}

static string BuildPreview(string? diff, string? newFileContents)
{
    if (!string.IsNullOrWhiteSpace(diff))
    {
        return BuildLinePreview(diff);
    }

    if (!string.IsNullOrWhiteSpace(newFileContents))
    {
        return BuildLinePreview(newFileContents);
    }

    return string.Empty;
}

static string BuildLinePreview(string text)
{
    var lines = text.Replace("\r", string.Empty)
        .Split('\n')
        .Take(MaxPreviewLines + 1)
        .ToList();

    var wasTrimmed = lines.Count > MaxPreviewLines;
    var visibleLines = wasTrimmed ? lines.Take(MaxPreviewLines) : lines;
    var preview = string.Join(Environment.NewLine, visibleLines);

    return wasTrimmed
        ? $"{preview}{Environment.NewLine}... more lines omitted"
        : preview;
}

static void ShowDemoFileSnapshot(string demoFilePath)
{
    Console.WriteLine();
    Console.WriteLine("Current DemoGreeter.cs");
    Console.WriteLine("----------------------");

    var lines = File.ReadLines(demoFilePath)
        .Take(20)
        .Select((line, index) => $"{index + 1}. {line}");

    foreach (var line in lines)
    {
        Console.WriteLine(line);
    }

    Console.WriteLine();
}

static string PrepareDemoWorkspace()
{
    var workspacePath = Path.Combine(
        Path.GetTempPath(),
        "CopilotSdk101",
        "PermissionHandlingDemo",
        Guid.NewGuid().ToString("N"));

    Directory.CreateDirectory(workspacePath);

    File.WriteAllText(
        Path.Combine(workspacePath, "DemoGreeter.cs"),
        """
        namespace PermissionHandlingDemo;

        public class DemoGreeter
        {
            public string BuildGreeting(string name)
            {
                var trimmed = name?.Trim() ?? string.Empty;
                if (trimmed.Length == 0)
                {
                    return "Hello there";
                }

                return "Hello " + trimmed + "!";
            }
        }
        """);

    File.WriteAllText(
        Path.Combine(workspacePath, "README.txt"),
        """
        Demo workspace for the Copilot SDK permission-handling sample.

        Ask the agent to refactor DemoGreeter.cs.
        The custom permission handler will stop before each read or write so you can approve, reject, or selectively auto-approve operations.
        """);

    return workspacePath;
}

sealed class PermissionPolicy
{
    private readonly object gate = new();
    private readonly StringComparison pathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private bool autoApproveReads;
    private bool autoApproveTargetWrites;

    public PermissionPolicy(string workspacePath, string targetFilePath)
    {
        ArgumentNullException.ThrowIfNull(workspacePath);
        ArgumentNullException.ThrowIfNull(targetFilePath);

        WorkspacePath = Path.GetFullPath(workspacePath);
        TargetFilePath = Path.GetFullPath(targetFilePath);
    }

    public string WorkspacePath { get; }

    public string TargetFilePath { get; }

    public bool ShouldAutoApproveRead(string path)
    {
        var fullPath = ResolvePath(path);

        lock (gate)
        {
            return autoApproveReads && IsWithinWorkspace(fullPath);
        }
    }

    public bool ShouldAutoApproveWrite(string path)
    {
        var fullPath = ResolvePath(path);

        lock (gate)
        {
            return autoApproveTargetWrites && string.Equals(fullPath, TargetFilePath, pathComparison);
        }
    }

    public bool CanConditionallyApproveRead(string path)
    {
        var fullPath = ResolvePath(path);
        return IsWithinWorkspace(fullPath);
    }

    public bool CanConditionallyApproveWrite(string path)
    {
        var fullPath = ResolvePath(path);
        return string.Equals(fullPath, TargetFilePath, pathComparison);
    }

    public void EnableReadAutoApproval()
    {
        lock (gate)
        {
            autoApproveReads = true;
        }
    }

    public void EnableTargetWriteAutoApproval()
    {
        lock (gate)
        {
            autoApproveTargetWrites = true;
        }
    }

    private bool IsWithinWorkspace(string fullPath)
    {
        var normalizedRoot = WorkspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(normalizedRoot, pathComparison);
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(path, WorkspacePath);
    }
}
