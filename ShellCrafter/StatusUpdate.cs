// ShellCrafter/StatusUpdate.cs
namespace ShellCrafter;

/// <summary>
/// Base type for all status updates reported during command execution.
/// </summary>
public abstract record StatusUpdate;

/// <summary>
/// Reported when the process has successfully started.
/// </summary>
/// <param name="ProcessId">The OS-assigned ID of the started process.</param>
public record ProcessStarted(int ProcessId) : StatusUpdate;

/// <summary>
/// Reported when a line of data is received on the standard output stream.
/// </summary>
/// <param name="Data">The line of text received (does not include newline characters).</param>
public record StdOutDataReceived(string Data) : StatusUpdate;

/// <summary>
/// Reported when a line of data is received on the standard error stream.
/// </summary>
/// <param name="Data">The line of text received (does not include newline characters).</param>
public record StdErrDataReceived(string Data) : StatusUpdate;

/// <summary>
/// Reported when the process has exited and all output/error streams are closed.
/// </summary>
/// <param name="Result">The final execution result, including exit code and captured output/error.</param>
public record ProcessExited(ExecutionResult Result) : StatusUpdate;

// We could add more later, e.g., ProcessAttemptingKill, ProcessKilled, etc.
