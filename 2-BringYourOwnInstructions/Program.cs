// Example 2: Bring your own instructions and skills.
// Inject a custom security-review skill and custom system instructions programmatically.

using GitHub.Copilot;

var repoPath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath("../../../..", AppContext.BaseDirectory);

await using var client = new CopilotClient(new CopilotClientOptions
{
    WorkingDirectory = repoPath,
});

await using var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = Environment.GetEnvironmentVariable("COPILOT_MODEL"),
    Streaming = true,
    OnPermissionRequest = PermissionHandler.ApproveAll,

    // Resolve explicit skill roots independently of the target repository's working directory.
    SkillDirectories = [Path.Combine(AppContext.BaseDirectory, "custom-config", "skills")],

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

    using var sub = session.On<SessionEvent>(evt =>
    {
        switch (evt)
        {
            case AssistantMessageDeltaEvent delta:
                Console.Write(delta.Data.DeltaContent);
                break;
            case SessionErrorEvent err:
                Console.WriteLine($"\n[ERROR] {err.Data}");
                break;
        }
    });

    await session.SendAndWaitAsync(prompt, timeout: TimeSpan.FromMinutes(5));

    Console.WriteLine("\n");
}
