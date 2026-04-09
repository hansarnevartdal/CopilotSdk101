// Example 1: Use the repo's .github instructions and skills automatically.
// Point CopilotClient at a git repo and let it discover copilot-instructions.md and skills.

using GitHub.Copilot.SDK;

// The Cwd tells the CLI where the repo lives — it auto-discovers .github/copilot-instructions.md
// and .github/copilot/skills/ from that location.
await using var client = new CopilotClient(new CopilotClientOptions
{
    Cwd = @"C:\git\Examples\AgenticCodingLoop",
});

await using var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-5.4",
    ReasoningEffort = "low",
    Streaming = true,
    OnPermissionRequest = PermissionHandler.ApproveAll,
});

Console.WriteLine("=== Copilot SDK — Repo Instructions ===");
Console.WriteLine("Using .github config from C:\\git\\Examples\\AgenticCodingLoop");
Console.WriteLine("Type your prompt (or 'exit' to quit):\n");

while (true)
{
    Console.Write("> ");
    var prompt = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(prompt) || prompt.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    var done = new TaskCompletionSource();

    using var sub = session.On(evt =>
    {
        switch (evt)
        {
            case AssistantMessageDeltaEvent delta:
                Console.Write(delta.Data.DeltaContent);
                break;
            case SessionIdleEvent:
                done.TrySetResult();
                break;
        }
    });

    await session.SendAsync(new MessageOptions { Prompt = prompt });
    await done.Task;

    Console.WriteLine("\n");
}
