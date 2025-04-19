
# ShellCrafter

A fluent C# builder API for executing external processes and shell commands with ease and clarity.

## Installation

> [!NOTE]
> **(Coming Soon: NuGet Package!)**

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
            .Command("dotnet")
            .WithArguments("--version")
            .ExecuteAsync();

        if (result.Succeeded)
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

## Examples

### Working Directory & Environment Variables

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

### Timeout and Cancellation

> [!WARNING]
> If a process exceeds the timeout or is cancelled, ShellCrafter can optionally kill the root process or entire tree.

```csharp
using var cts = new CancellationTokenSource();

try
{
    ExecutionResult result = await ShellCrafter
        .Command("long_running_script.sh")
        .WithTimeout(TimeSpan.FromSeconds(10))
        .ExecuteAsync(cancellationToken: cts.Token, killMode: KillMode.ProcessTree);

    Console.WriteLine("Script finished successfully.");
}
catch (TimeoutException ex)
{
    Console.WriteLine($"Command timed out: {ex.Message}");
}
catch (OperationCanceledException)
{
    Console.WriteLine("Command was cancelled externally.");
}
```

### Progress Reporting

> [!TIP]
> Register a progress handler to get updates like output lines or process state.

```csharp
var progressHandler = new Progress<StatusUpdate>(update =>
{
    switch (update)
    {
        case ProcessStarted ps: Console.WriteLine($"PROC: Started PID {ps.ProcessId}"); break;
        case StdOutDataReceived so: Console.WriteLine($"OUT: {so.Data}"); break;
        case StdErrDataReceived se: Console.WriteLine($"ERR: {se.Data}"); break;
        case ProcessExited pe: Console.WriteLine($"PROC: Exited Code {pe.Result.ExitCode}"); break;
    }
});
ExecutionResult result = await ShellCrafter
    .Command("ping")
    .WithArguments("google.com")
    .WithProgress(progressHandler)
    .ExecuteAsync();
```

> [!NOTE]
> StdOut/StdErr events aren’t raised when output is piped using `PipeStandardOutputTo`.

### Standard Input

```csharp
ExecutionResult resultString = await ShellCrafter
    .Command("grep")
    .WithArguments("error")
    .WithStandardInput($"Line1\nErrorLine2\nLine3")
    .ExecuteAsync();
Console.WriteLine($"Grep Result (String): {resultString.StandardOutput}");

using var fileStream = File.OpenRead("input.txt");
ExecutionResult resultStream = await ShellCrafter
    .Command("sort")
    .WithStandardInput(fileStream)
    .ExecuteAsync();
Console.WriteLine($"Sort Result (Stream): {resultStream.StandardOutput}");
```

### Output/Error Piping

```csharp
using var outputCapture = new MemoryStream();
using var errorCapture = new MemoryStream();

ExecutionResult resultPipe = await ShellCrafter
    .Command("complex_tool")
    .WithArguments("-v", "--output-mode=text")
    .PipeStandardOutputTo(outputCapture, captureInternal: false)
    .PipeStandardErrorTo(errorCapture, captureInternal: false)
    .ExecuteAsync();

outputCapture.Position = 0;
using var outReader = new StreamReader(outputCapture);
string capturedOutText = await outReader.ReadToEndAsync();
Console.WriteLine($"Captured StdOut Length: {capturedOutText.Length}");
```

## API Overview

> [!IMPORTANT]
> Here's your toolbox of fluent API calls. Combine them to shape the behavior you want.

- `Command(string executable)`
- `.WithArguments(params string[] args)`
- `.InWorkingDirectory(string path)`
- `.WithEnvironmentVariable(string key, string? value)`
- `.WithEnvironmentVariables(IDictionary<string, string?> variables)`
- `.WithStandardInput(string input)`
- `.WithStandardInput(Stream inputStream)`
- `.PipeStandardOutputTo(Stream target, bool captureInternal = false)`
- `.PipeStandardErrorTo(Stream target, bool captureInternal = false)`
- `.WithProgress(IProgress<StatusUpdate> progress)`
- `.WithTimeout(TimeSpan duration)`
- `.ExecuteAsync(CancellationToken cancellationToken = default, KillMode killMode = KillMode.NoKill)`

### ExecutionResult Object

- `ExitCode (int)`
- `StandardOutput (string)`
- `StandardError (string)`
- `Succeeded (bool)`

### StatusUpdate (Progress)

- `ProcessStarted(int ProcessId)`
- `StdOutDataReceived(string Data)`
- `StdErrDataReceived(string Data)`
- `ProcessExited(ExecutionResult Result)`

### KillMode

> [!CAUTION]
> You must choose how aggressively to cancel your process!

- `KillMode.NoKill` – Don't terminate, just cancel await.
- `KillMode.RootProcess` – Kill the root process only.
- `KillMode.ProcessTree` – Kill everything in the spawned tree.
