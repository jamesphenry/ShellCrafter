# ShellCrafter

A fluent C# builder API for executing external processes and shell commands with ease and clarity.

## Installation

Currently, clone the repository and build the `ShellCrafter` project. (Future: Add instructions for NuGet package when available).

## Basic Usage

`ShellCrafter` provides a fluent interface to configure and execute external processes.

```csharp
using ShellCrafter;
using System.Threading.Tasks;

public class Example
{
    public async Task RunCommand()
    {
        ExecutionResult result = await ShellCrafter
            .Command("dotnet")                       // Specify the executable
            .WithArguments("--version")              // Add command line arguments
            .InWorkingDirectory("/path/to/project")  // Optional: Set working directory
            .WithEnvironmentVariable("MY_VAR", "my_value") // Optional: Set environment variables
            .ExecuteAsync();                         // Execute asynchronously

        if (result.ExitCode == 0)
        {
            Console.WriteLine("Command succeeded!");
            Console.WriteLine(<span class="math-inline">"Output\:\\n\{result\.StandardOutput\}"\);
\}
else
\{
Console\.WriteLine\(</span>"Command failed with exit code: {result.ExitCode}");
            Console.WriteLine($"Error Output:\n{result.StandardError}");
        }
    }
}
```

## API Overview

* **`ShellCrafter.Command(string executable)`**: ...
* **`.WithArguments(params string[] args)`**: ...
* **`.InWorkingDirectory(string path)`**: ...
* **`.WithEnvironmentVariable(string key, string? value)`**: ...
* **`.WithEnvironmentVariables(IDictionary<string, string?> variables)`**: ...
* **`.WithStandardInput(string input)`**: ...
* **`.WithProgress(IProgress<StatusUpdate> progress)`**: ...
* **`.WithTimeout(TimeSpan duration)`**: Sets a maximum execution duration. Throws `TimeoutException` if exceeded. *(New)*
* **`.ExecuteAsync(CancellationToken cancellationToken = default, bool killOnCancel = false)`**: Executes the configured command asynchronously... *(Updated note: killOnCancel also applies on timeout)*

> `killOnCancel: true` will also attempt to kill the process if the timeout specified by `.WithTimeout()` is exceeded and cancellation occurs due to that timeout.)
## Progress Reporting

You can receive status updates during command execution by providing an `IProgress<StatusUpdate>` handler via the `.WithProgress()` method. The following `StatusUpdate` types (defined as records in the `ShellCrafter` namespace) can be reported:

* **`ProcessStarted(int ProcessId)`**: Reported once the process has successfully started. Includes the OS process ID.
* **`StdOutDataReceived(string Data)`**: Reported for each line of data received on standard output. The data is trimmed of leading/trailing whitespace.
* **`StdErrDataReceived(string Data)`**: Reported for each line of data received on standard error. The data is trimmed of leading/trailing whitespace.
* **`ProcessExited(ExecutionResult Result)`**: Reported once the process has exited normally (not via cancellation) and all output has been processed. Includes the final `ExecutionResult`.

**Example:**

```csharp
var progressHandler = new Progress<StatusUpdate>(update =>
{
    switch (update)
    {
        case ProcessStarted ps:
            Console.WriteLine(<span class="math-inline">"Process started with ID\: \{ps\.ProcessId\}"\);
break;
case StdOutDataReceived so\:
Console\.WriteLine\(</span>"OUT: {so.Data}");
            break;
        case StdErrDataReceived se:
            Console.WriteLine(<span class="math-inline">"ERR\: \{se\.Data\}"\);
break;
case ProcessExited pe\:
Console\.WriteLine\(</span>"Process exited with code: {pe.Result.ExitCode}");
            break;
    }
});

ExecutionResult result = await ShellCrafter
    .Command("some_command")
    .WithProgress(progressHandler)
    .ExecuteAsync();
```

# Result Object
- The ExecuteAsync method returns an ExecutionResult record with the following properties:

- ExitCode (int): The exit code returned by the process. 0 typically indicates success.
- StandardOutput (string): The captured standard output (stdout) of the process, trimmed of leading/trailing whitespace.
- StandardError (string): The captured standard error (stderr) of the process, trimmed of leading/trailing whitespace.

# Contributing
(Add contribution guidelines later if desired)

# License
(Specify a license later if desired, e.g., MIT)

**2. Feature Discussion Markdown Export**

Here's the summary of our development conversation for this feature. You can save this as something like `feature-shellcrafter-core-functionality-log.md`:

# Feature Log: ShellCrafter Core Functionality

This log captures the discussion and development steps for creating the initial `ShellCrafter` fluent builder.

**User Preferences & Goals:** SOLID, Design Patterns (Builder), KISS, Fluent Design, Extension Methods, C#, TDD (xUnit, NFluent), GitFlow, `Namespace;`, Top-level statements, `README.md`, Markdown Export.

## 1. Project Setup & Git Flow

* Decided on repository name: `ShellCrafter`.
* Description: "A fluent C# builder API for executing external processes and shell commands with ease and clarity."
* User confirmed using `git-flow-avh`.
* Started feature branch: `git flow feature start shellcrafter-basic-execution-failing-test`

## 2. TDD Setup (Custom Spec & xunit.runner.json)

* Implemented custom `[Spec]` attribute inheriting from `[Fact]` for readable test names (`TestMethod_Name` -> `Test Method Name`).
* Configured `xunit.runner.json` for parallel execution and diagnostic messages:
    ```json
    {
      "$schema": "[https://xunit.net/schema/current/xunit.runner.schema.json](https://xunit.net/schema/current/xunit.runner.schema.json)",
      "parallelizeAssembly": true,
      "parallelizeTestCollections": true,
      "diagnosticMessages": true,
      "methodDisplay": "method" 
    }
    ```
    *(Note: `methodDisplay` is often overridden by `[Spec]`'s `DisplayName`)*

## 3. Feature: Basic Command Execution (TDD Cycle 1)

* **Test:** `Should_execute_basic_command_successfully`
    * Goal: Run `dotnet --version` and check for `ExitCode == 0`.
    * API Sketch: `ShellCrafter.Command("dotnet").WithArguments("--version").ExecuteAsync()`
    * Result Type: `ExecutionResult(int ExitCode, string StandardOutput, string StandardError)`
* **Initial Code (Placeholders):** Created `ShellCrafter`, `CommandBuilder`, `ExecutionResult`. `ExecuteAsync` returned dummy `ExitCode = -1`.
* **RED:** Test failed, expected 0, got -1.
* **Implementation:** Implemented `ExecuteAsync` using `System.Diagnostics.Process`.
    * Configured `ProcessStartInfo` (FileName, Arguments, Redirects, UseShellExecute=false, CreateNoWindow=true).
    * Used `process.Start()`, `BeginOutputReadLine()`, `BeginErrorReadLine()`.
    * Used `StringBuilder` and `TaskCompletionSource` for async stream reading.
    * Used `process.WaitForExitAsync()` and waited for stream completion tasks.
    * Returned `new ExecutionResult` with actual data.
    * Used `.TrimEnd()` initially on output strings.
* **GREEN:** Test passed.
* **Refactor:** Code looked okay for initial step.
* **Commit:** `feat: Implement basic command execution via ShellCrafter`

## 4. Feature: Standard Output Capture (TDD Cycle 2)

* **Test:** `Should_capture_standard_output_correctly`
    * Goal: Run platform-specific `echo` (`cmd /c echo ...` or `sh -c 'echo ...'`) and check if `StandardOutput` matches the expected string. Used OS detection (`RuntimeInformation.IsOSPlatform`).
    * Passed arguments individually: `WithArguments("/c", "echo", expectedOutput)` or `WithArguments("-c", $"echo {expectedOutput}")`.
* **Initial Run (Prediction: Green):** Test failed (RED).
    * Failure: Actual output was `"ShellCrafter_Test_Output"` (extra trailing quote on Windows).
* **Debug/Fix:** Hypothesized issue with `.TrimEnd()`. Changed implementation in `ExecuteAsync` to use `.Trim()` on both `StandardOutput` and `StandardError` strings in the final `ExecutionResult`.
* **GREEN:** Test passed after changing to `.Trim()`.
* **Refactor:** `Trim()` seems like a better default.
* **Commit:** `feat: Capture and verify standard output`

## 5. Feature: Standard Error Capture (TDD Cycle 3)

* **Test:** `Should_capture_standard_error_correctly`
    * Goal: Run platform-specific `echo` redirected to stderr (`1>&2`) and check if `StandardError` matches, while `StandardOutput` is empty.
* **Initial Run (Prediction: Green):** Test passed (GREEN). Implementation already handled stderr capture correctly.
* **Refactor:** None needed.
* **Commit:** `feat: Capture and verify standard error output` (Test commit: `test: Verify standard error output capture`) - *Correction: Commit message should reflect feature/test addition.* Adjusted commit message: `feat: Capture and verify standard error output`

## 6. Feature: Set Working Directory (TDD Cycle 4)

* **Test:** `Should_execute_command_in_specified_working_directory`
    * Goal: Create temp dir, use `.InWorkingDirectory(path)`, run `cd` (Win) or `pwd` (Unix), check `StandardOutput` matches temp dir path. Used `try...finally` for cleanup.
    * API: Added `.InWorkingDirectory(string path)` method to builder.
* **Initial Code (Placeholders):** Added `_workingDirectory` field and `InWorkingDirectory` method to `CommandBuilder`, but didn't use the field in `ExecuteAsync`.
* **RED:** Test failed, output showed default directory.
* **Implementation:** Added `WorkingDirectory = _workingDirectory` to `ProcessStartInfo` initialization in `ExecuteAsync`.
* **GREEN:** Test passed.
* **Refactor:** Decided against pre-validating path in `InWorkingDirectory`, letting `Process.Start` handle errors.
* **Commit:** `feat: Allow specifying working directory for execution`

## 7. Feature: Handle Non-Zero Exit Codes (TDD Cycle 5)

* **Test:** `Should_return_non_zero_exit_code_for_failing_command`
    * Goal: Run command designed to fail (`cmd /c "exit /b 99"` or `sh -c "exit 99"`), check `result.ExitCode` matches `99`.
* **Initial Run (Prediction: Green):** Test passed (GREEN). Implementation already returned the actual exit code correctly.
* **Refactor:** None needed. Test served as validation.
* **Commit:** `test: Verify non-zero exit code is returned correctly`

## 8. Feature: Pass Environment Variables (TDD Cycle 6)

* **Test:** `Should_execute_command_with_specified_environment_variable`
    * Goal: Define test var/value, use `.WithEnvironmentVariable(key, value)`, run platform `echo` for that variable (`%VAR%` or `$VAR`), check `StandardOutput` matches value.
    * API: Added `.WithEnvironmentVariable(string key, string? value)` method.
* **Initial Code (Placeholders):** Added `_environmentVariables` dictionary and `WithEnvironmentVariable` method to `CommandBuilder`, but didn't use the dictionary in `ExecuteAsync`.
* **RED:** Test failed, output was empty line (variable not set).
* **Implementation:** Added loop in `ExecuteAsync` after `ProcessStartInfo` creation to iterate `_environmentVariables` and set values on `processStartInfo.EnvironmentVariables`.
* **GREEN:** Test passed.
* **Refactor:** Considered adding bulk overload `WithEnvironmentVariables`, deferred for now.
* **Commit:** `feat: Allow passing environment variables to process`

## 9. Feature Completion

* Finished Git Flow feature: `git flow feature finish shellcrafter-basic-execution-failing-test`
