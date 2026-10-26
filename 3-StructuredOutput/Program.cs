// Example 3: Structured output via custom tools.
// Use an AIFunction as an "output schema" — the agent calls our tool with strongly typed data,
// giving us a typed C# object when the agent calls the tool.

using System.ComponentModel;
using GitHub.Copilot;
using Microsoft.Extensions.AI;
using StructuredOutput;

var repoPath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath("../../../..", AppContext.BaseDirectory);

SecurityResult? result = null;

// Ask the agent to report through this tool; the prompt cannot guarantee a call.
// The parameters become the structured output schema.
var reportTool = AIFunctionFactory.Create(
    ([Description("Whether the repository is secure")] bool isSecure,
     [Description("List of security vulnerabilities found")] string[] vulnerabilities) =>
    {
        result = new SecurityResult(isSecure, [.. vulnerabilities]);
        return "Security report submitted successfully.";
    },
    "report_security_result",
    "Report the security analysis result for the repository");

await using var client = new CopilotClient(new CopilotClientOptions
{
    WorkingDirectory = repoPath,
});

await using var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = Environment.GetEnvironmentVariable("COPILOT_MODEL"),
    Streaming = false, // We only care about the tool call, not streamed text
    OnPermissionRequest = PermissionHandler.ApproveAll,
    Tools = [reportTool],
    SystemMessage = new SystemMessageConfig
    {
        Mode = SystemMessageMode.Append,
        Content = """
            Analyze the repository for security vulnerabilities.
            When done, you MUST call the report_security_result tool with your findings.
            Set isSecure to false if any vulnerabilities are found.
            List each vulnerability as a short description.
            """,
    },
});

Console.WriteLine("=== Copilot SDK — Structured Output ===");
Console.WriteLine($"Analyzing {repoPath} for security vulnerabilities...\n");

// Single-shot: send the prompt and wait for the agent to finish (calls our tool)
await session.SendAndWaitAsync(
    new MessageOptions { Prompt = "Analyze this repository for security vulnerabilities." },
    timeout: TimeSpan.FromMinutes(30));

// Print the strongly typed result captured by our tool
if (result is not null)
{
    Console.WriteLine("╔══════════════════════════════╗");
    Console.WriteLine("║     Security Report          ║");
    Console.WriteLine("╚══════════════════════════════╝");
    Console.WriteLine($"  Secure: {(result.IsSecure ? "Yes ✅" : "No ❌")}");
    Console.WriteLine($"  Vulnerabilities: {result.Vulnerabilities.Count}");

    foreach (var vuln in result.Vulnerabilities)
    {
        Console.WriteLine($"    • {vuln}");
    }
}
else
{
    Console.WriteLine("No security report was generated.");
}
