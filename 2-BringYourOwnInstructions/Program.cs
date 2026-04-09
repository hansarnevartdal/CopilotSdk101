// Example 2: Bring your own instructions and skills.
// Inject a custom security-review skill and custom system instructions programmatically.

using GitHub.Copilot.SDK;

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

    // Load custom skills from our own folder instead of the repo's .github/copilot/skills
    SkillDirectories = ["./custom-config/skills/security-review"],

    // Append extra instructions to the default system prompt
    SystemMessage = new SystemMessageConfig
    {
        Mode = SystemMessageMode.Append,
        Content = """
            You are a security-focused code reviewer.
            Always consider OWASP Top 10 when reviewing code.
            Flag any hardcoded secrets, missing input validation, or auth issues.
            """,
    },
});

Console.WriteLine("=== Copilot SDK — Custom Instructions & Skills ===");
Console.WriteLine("Loaded: security-review skill + custom system instructions");
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
            case SessionErrorEvent err:
                Console.WriteLine($"\n[ERROR] {err.Data}");
                done.TrySetResult();
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
