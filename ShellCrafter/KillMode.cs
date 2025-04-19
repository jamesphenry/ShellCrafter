// ShellCrafter/KillMode.cs (or similar file)
namespace ShellCrafter;

/// <summary>
/// Specifies the behavior when cancellation (external or timeout) occurs during command execution.
/// </summary>
public enum KillMode
{
    /// <summary>
    /// Do not attempt to kill the process upon cancellation. Only stop waiting.
    /// </summary>
    NoKill = 0,

    /// <summary>
    /// Attempt to kill only the root process upon cancellation.
    /// </summary>
    RootProcess = 1,

    /// <summary>
    /// Attempt to kill the root process and its entire descendant process tree upon cancellation.
    /// Requires .NET 5 or later.
    /// </summary>
    ProcessTree = 2
}