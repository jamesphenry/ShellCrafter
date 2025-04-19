// ShellCrafter/ExecutionResult.cs
namespace ShellCrafter;

// Using a record for concise, immutable result data
public record ExecutionResult(int ExitCode, string StandardOutput, string StandardError);

