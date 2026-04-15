// Example 5: Reuse one session across multiple turns so the agent remembers earlier context.
// The guided demo shows a file review, a follow-up question, and a fix request in the same session.

using GitHub.Copilot.SDK;

var repoPath = FindRepoRoot(AppContext.BaseDirectory);

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
    SystemMessage = new SystemMessageConfig
    {
        Mode = SystemMessageMode.Append,
        Content = """
            You are an interactive code-review assistant used in a session-history demo.
            Every prompt in this sample runs in the same session.
            When the user asks a follow-up question that depends on an earlier answer, explicitly mention the earlier finding or recommendation before you continue.
            Keep answers concise, practical, and focused on the repository files being discussed.
            Do not modify files unless the user explicitly asks you to.
            """,
    },
});

var guidedTurns = new (string Title, string Prompt)[]
{
    (
        "Turn 1 — Review a file",
        "Review 1-RepoInstructions/Program.cs as if you were helping a newcomer understand the sample. What does it do well, and what is one improvement you would suggest?"
    ),
    (
        "Turn 2 — Ask a follow-up",
        "You just suggested one improvement for 1-RepoInstructions/Program.cs. Which exact part of the file should be improved first, and why? Refer back to your earlier finding."
    ),
    (
        "Turn 3 — Request a fix",
        "Based on the improvement you recommended in the previous turns, draft the exact comment or console text you would change in 1-RepoInstructions/Program.cs, but do not modify files."
    ),
};

var turnCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var waitingForTurn = false;

using var subscription = session.On(evt =>
{
    switch (evt)
    {
        case AssistantMessageDeltaEvent delta:
            Console.Write(delta.Data.DeltaContent);
            break;
        case SessionErrorEvent error:
            if (waitingForTurn)
            {
                turnCompleted.TrySetException(new InvalidOperationException(error.Data?.ToString() ?? "The session reported an error."));
            }

            Console.WriteLine($"\n[ERROR] {error.Data}");
            break;
        case SessionIdleEvent:
            if (waitingForTurn)
            {
                turnCompleted.TrySetResult();
            }
            break;
    }
});

Console.WriteLine("=== Copilot SDK — Conversational Agent with Session History ===");
Console.WriteLine($"Target repo: {repoPath}");
Console.WriteLine($"Session ID: {session.SessionId}");
Console.WriteLine("This example reuses one session across multiple SendAsync calls.");
Console.WriteLine("It starts with a guided three-turn demo: review a file, ask a follow-up, then request a fix.\n");
Console.WriteLine("Press Enter to run the guided demo, type 'skip' to jump to freeform chat, or 'exit' to quit.\n");

Console.Write("> ");
var startupChoice = Console.ReadLine();

if (startupChoice?.Equals("exit", StringComparison.OrdinalIgnoreCase) == true)
{
    return;
}

if (!string.Equals(startupChoice, "skip", StringComparison.OrdinalIgnoreCase))
{
    await RunGuidedDemoAsync();
}

Console.WriteLine("The session is still live, so you can keep asking follow-up questions.");
Console.WriteLine("Try referencing the earlier discussion about 1-RepoInstructions/Program.cs.");
Console.WriteLine("Type 'demo' to rerun the guided conversation or 'exit' to quit.\n");

while (true)
{
    Console.Write("> ");
    var prompt = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(prompt))
    {
        continue;
    }

    if (prompt.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    if (prompt.Equals("demo", StringComparison.OrdinalIgnoreCase))
    {
        await RunGuidedDemoAsync();
        continue;
    }

    await RunTurnAsync("Custom turn", prompt);
}

return;

async Task RunGuidedDemoAsync()
{
    Console.WriteLine();
    Console.WriteLine("=== Guided demo ===");

    foreach (var turn in guidedTurns)
    {
        await RunTurnAsync(turn.Title, turn.Prompt);
    }
}

async Task RunTurnAsync(string title, string prompt)
{
    Console.WriteLine();
    Console.WriteLine(title);
    Console.WriteLine($"User: {prompt}");
    Console.Write("Assistant: ");

    turnCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    waitingForTurn = true;

    try
    {
        await session.SendAsync(new MessageOptions { Prompt = prompt });
        await turnCompleted.Task;
    }
    finally
    {
        waitingForTurn = false;
    }

    Console.WriteLine("\n");
}

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
