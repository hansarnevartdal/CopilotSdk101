// Example 1: Use the repo's .github instructions and skills automatically.
// Point CopilotClient at a git repo and let it discover copilot-instructions.md and skills.

using GitHub.Copilot;

var repoPath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath("../../../..", AppContext.BaseDirectory);

// WorkingDirectory selects the repo; EnableConfigDiscovery opts into its configuration and skills.
await using var client = new CopilotClient(new CopilotClientOptions
{
    WorkingDirectory = repoPath,
});

await using var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = Environment.GetEnvironmentVariable("COPILOT_MODEL"),
    EnableConfigDiscovery = true,
    // The standalone runtime has no extension launch provider; this sample loads repo configuration.
#pragma warning disable GHCP001
    RequestExtensions = false,
#pragma warning restore GHCP001
    Streaming = true,
    OnPermissionRequest = PermissionHandler.ApproveAll,
});

Console.WriteLine("=== Copilot SDK — Repo Instructions ===");
Console.WriteLine($"Using repository configuration from {repoPath}");
Console.WriteLine("Type your prompt (or 'exit' to quit):\n");

while (true)
{
    Console.Write("> ");
    var prompt = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(prompt) || prompt.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    using var sub = session.On<SessionEvent>(evt =>
    {
        switch (evt)
        {
            case AssistantMessageDeltaEvent delta:
                Console.Write(delta.Data.DeltaContent);
                break;
        }
    });

    // Waiting for completion also works with streamed delta events.
    await session.SendAndWaitAsync(prompt, timeout: TimeSpan.FromMinutes(5));

    Console.WriteLine("\n");
}
