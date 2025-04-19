// ShellCrafter/CommandExecutor.cs
namespace ShellCrafter;

/// <summary>
/// Holds all configuration settings for a command to be executed.
/// Populated by the CommandBuilder.
/// </summary>
internal class CommandConfiguration
{
    // --- Core Process Info ---
    internal string Executable { get; } // Set only once
    internal List<string> Arguments { get; } = new();
    internal Dictionary<string, string?> EnvironmentVariables { get; } = new();
    internal string? WorkingDirectory { get; set; } = null;

    // --- Input ---
    // Only one of these should be non-null at execution time
    internal string? StandardInputString { get; set; } = null;
    internal Stream? StandardInputStream { get; set; } = null;

    // --- Output/Error Handling ---
    internal Stream? StdOutPipeTarget { get; set; } = null;
    internal Stream? StdErrPipeTarget { get; set; } = null;
    internal bool CaptureStdOut { get; set; } = true;
    internal bool CaptureStdErr { get; set; } = true;

    // --- Execution Control ---
    internal TimeSpan? Timeout { get; set; } = null;
    // Note: KillMode is passed directly to ExecuteAsync, not stored here.

    // --- Feedback ---
    internal IProgress<StatusUpdate>? ProgressHandler { get; set; } = null;

    // --- Constructor ---
    internal CommandConfiguration(string executable)
    {
        Executable = executable; // Required at creation
    }
}
