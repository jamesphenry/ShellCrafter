# ShellCrafter

A fluent C# builder API for executing external processes and shell commands with ease and clarity.

## Installation

**(Coming Soon: NuGet Package!)**

Currently, clone the repository and reference the `ShellCrafter` project directly.

## Basic Usage

`ShellCrafter` provides a fluent interface to configure and execute external processes. Get the exit code and captured output/error easily.

```csharp
using ShellCrafter;
using System.Threading.Tasks;

public class Example
{
    public async Task RunCommandSimple()
    {
        ExecutionResult result = await ShellCrafter
            .Command("dotnet") // Specify the executable
            .WithArguments("--version") // Add command line arguments
            .ExecuteAsync(); // Execute asynchronously

        if (result.Succeeded) // Helper property: ExitCode == 0
        {
            Console.WriteLine("Command succeeded!");
            Console.WriteLine($"Output:\n{result.StandardOutput}");
        }
        else
        {
            Console.WriteLine($"Command failed with exit code: {result.ExitCode}");
            Console.WriteLine($"Error Output:\n{result.StandardError}");
        }
    }
}
```

# Examples
Setting Working Directory & Environment Variables
```csharp
ExecutionResult result = await ShellCrafter
    .Command("git")
    .WithArguments("status")
    .InWorkingDirectory("C:\\Projects\\MyRepo")
    .WithEnvironmentVariable("GIT_TERMINAL_PROMPT", "0")
    .WithEnvironmentVariables(new Dictionary<string, string?> { ["VAR_A"] = "ValA", ["VAR_B"] = null })
    .ExecuteAsync();

Console.WriteLine($"Git Status Exit Code: {result.ExitCode}");
```

# Timeout and Cancellation (with Kill)
```csharp
using var cts = new CancellationTokenSource();
// cts.CancelAfter(TimeSpan.FromSeconds(5)); // Optional external cancellation

try
{
    ExecutionResult result = await ShellCrafter
        .Command("long_running_script.sh")
        .WithTimeout(TimeSpan.FromSeconds(10)) // Add a 10-second timeout
        .ExecuteAsync(cancellationToken: cts.Token, killMode: KillMode.ProcessTree); // Pass token, kill process tree on cancel/timeout

    Console.WriteLine("Script finished successfully.");
}
catch (TimeoutException ex)
{
    Console.WriteLine($"Command timed out: {ex.Message}");
}
catch (OperationCanceledException) // Catches external cancellation
{
    Console.WriteLine("Command was cancelled externally.");
}
```

# Progress Reporting
```csharp
var progressHandler = new Progress<StatusUpdate>(update =>
{
    switch (update)
    {
        case ProcessStarted ps: Console.WriteLine($"PROC: Started PID {ps.ProcessId}"); break;
        case StdOutDataReceived so: Console.WriteLine($"OUT: {so.Data}"); break; // Data is trimmed
        case StdErrDataReceived se: Console.WriteLine($"ERR: {se.Data}"); break; // Data is trimmed
        case ProcessExited pe: Console.WriteLine($"PROC: Exited Code {pe.Result.ExitCode}"); break;
    }
});
ExecutionResult result = await ShellCrafter
    .Command("ping")
    .WithArguments("google.com")
    .WithProgress(progressHandler)
    .ExecuteAsync();

```

> (Note: StdOutDataReceived/StdErrDataReceived are not reported for streams being piped via PipeStandardOutputTo/PipeStandardErrorTo)

# Standard Input (String and Stream)
```csharp
// String Input
ExecutionResult resultString = await ShellCrafter
    .Command("grep")
    .WithArguments("error")
    .WithStandardInput($"Line1\nErrorLine2\nLine3")
    .ExecuteAsync();
Console.WriteLine($"Grep Result (String): {resultString.StandardOutput}"); // Output: ErrorLine2

// Stream Input (e.g., from a file)
using var fileStream = File.OpenRead("input.txt");
ExecutionResult resultStream = await ShellCrafter
    .Command("sort")
    .WithStandardInput(fileStream) // ShellCrafter does NOT dispose the stream
    .ExecuteAsync();
Console.WriteLine($"Sort Result (Stream): {resultStream.StandardOutput}");
```

# Output/Error Piping
```csharp
using var outputCapture = new MemoryStream();
using var errorCapture = new MemoryStream();

// Pipe stdout to outputCapture stream, disable internal capture
// Pipe stderr to errorCapture stream, disable internal capture
ExecutionResult resultPipe = await ShellCrafter
    .Command("complex_tool")
    .WithArguments("-v", "--output-mode=text")
    .PipeStandardOutputTo(outputCapture, captureInternal: false) 
    .PipeStandardErrorTo(errorCapture, captureInternal: false)
    .ExecuteAsync();

Console.WriteLine($"Tool Exit Code: {resultPipe.ExitCode}");
// Check.That(resultPipe.StandardOutput).IsEmpty(); // Internal capture disabled
// Check.That(resultPipe.StandardError).IsEmpty();  // Internal capture disabled

// Process captured streams
outputCapture.Position = 0;
using var outReader = new StreamReader(outputCapture);
string capturedOutText = await outReader.ReadToEndAsync();
Console.WriteLine($"Captured StdOut Length: {capturedOutText.Length}"); 
// Similar processing for errorCapture...
```

# API Overview
ShellCrafter.Command(string executable): Static entry point to start building a command.
.WithArguments(params string[] args): Adds arguments to the command line. Handles basic escaping for spaces/quotes.
.InWorkingDirectory(string path): Sets the working directory for the process.
.WithEnvironmentVariable(string key, string? value): Adds or updates a single environment variable.
.WithEnvironmentVariables(IDictionary<string, string?> variables): Adds or updates multiple environment variables.
.WithStandardInput(string input): Provides string data to the process's standard input. Clears any previously set stream input.
.WithStandardInput(Stream inputStream): Provides data from a Stream to the process's standard input. Clears any previously set string input. ShellCrafter does not dispose the provided stream.
.PipeStandardOutputTo(Stream target, bool captureInternal = false): Pipes standard output directly to the provided writable stream. If captureInternal is false (default), ExecutionResult.StandardOutput will be empty. Caller manages target stream disposal.
.PipeStandardErrorTo(Stream target, bool captureInternal = false): Pipes standard error directly to the provided writable stream. If captureInternal is false (default), ExecutionResult.StandardError will be empty. Caller manages target stream disposal.
.WithProgress(IProgress<StatusUpdate> progress): Registers a handler to receive status updates during execution (see Progress Reporting section).
.WithTimeout(TimeSpan duration): Sets a maximum execution duration. Throws TimeoutException if exceeded. Timeout.InfiniteTimeSpan disables the timeout.
.ExecuteAsync(CancellationToken cancellationToken = default, KillMode killMode = KillMode.NoKill): Executes the configured command asynchronously. Returns an ExecutionResult. See Cancellation Behavior section for KillMode.
Result Object (ExecutionResult)
The ExecuteAsync method returns an ExecutionResult record with the following properties:
ExitCode (int): The exit code returned by the process. 0 typically indicates success.
StandardOutput (string): The captured standard output (stdout), trimmed of leading/trailing whitespace. Will be empty if stdout was piped with captureInternal: false.
StandardError (string): The captured standard error (stderr), trimmed of leading/trailing whitespace. Will be empty if stderr was piped with captureInternal: false.
Succeeded (bool property): Returns true if ExitCode is 0.
Progress Reporting (StatusUpdate)
When using .WithProgress(), the handler receives instances derived from the base StatusUpdate record:
ProcessStarted(int ProcessId): Process has started.
StdOutDataReceived(string Data): Line received on stdout (data is trimmed, not reported if stdout is piped).
StdErrDataReceived(string Data): Line received on stderr (data is trimmed, not reported if stderr is piped).
ProcessExited(ExecutionResult Result): Process exited normally (not via cancellation).
Cancellation Behavior (KillMode)
The ExecuteAsync method accepts a KillMode enum parameter to control behavior when cancellation occurs (either via the passed CancellationToken or an execution timeout specified by .WithTimeout()):
KillMode.NoKill (Default): Only stops waiting; the process itself is not terminated by ShellCrafter. OperationCanceledException is thrown for external cancellation, TimeoutException for timeouts.
KillMode.RootProcess: Attempts to kill the main process. Throws OperationCanceledException or TimeoutException.
KillMode.ProcessTree: Attempts to kill the main
