// ShellCrafter.Tests/ShellCrafterExecutionTests.cs
namespace ShellCrafter.Tests;

using Xunit;
using NFluent;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;

// Helper class within the test file to collect updates
file class StatusUpdateCollector : IProgress<StatusUpdate>
{
    public List<StatusUpdate> Updates { get; } = new();
    public void Report(StatusUpdate value) => Updates.Add(value);
}

public class ShellCrafterExecutionTests
{
    [Spec]
    public async Task Should_Execute_Basic_Command_Successfully()
    {
        // Arrange: Define the command using our desired fluent API
        var commandTask = ShellCrafter // The main static entry point or builder starter
            .Command("dotnet")       // Specify the executable
            .WithArguments("--version") // Add arguments
            .ExecuteAsync();          // Execute the command asynchronously

        // Act: Await the result
        var result = await commandTask;

        // Assert: Check for successful execution using NFluent
        Check.That(result.ExitCode).IsEqualTo(0);
    }

    [Spec]
    public async Task Should_capture_standard_output_correctly()
    {
        // Arrange
        string executable;
        List<string> arguments = new(); // Use a list to gather args
        const string expectedOutput = "ShellCrafter_Test_Output";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            executable = "cmd";
            arguments.Add("/c");      // Argument 1 for cmd
            arguments.Add("echo");    // Argument 2 for cmd
            arguments.Add(expectedOutput); // Argument 3 for cmd
        }
        else // Assume Linux/macOS or other POSIX-like shells
        {
            executable = "sh";
            arguments.Add("-c");      // Argument 1 for sh
            // For 'sh -c', the command to execute is typically a single string argument
            arguments.Add($"echo {expectedOutput}"); // Argument 2 for sh
        }

        // Act: Execute the appropriate command
        var result = await ShellCrafter
            .Command(executable)
            // Pass arguments using the collected list
            .WithArguments(arguments.ToArray())
            .ExecuteAsync();

        // Assert
        Check.That(result.ExitCode).IsEqualTo(0);
        Check.That(result.StandardOutput).IsEqualTo(expectedOutput);
        Check.That(result.StandardError).IsEmpty();
    }

    [Spec]
    public async Task Should_capture_standard_error_correctly()
    {
        // Arrange
        string executable;
        List<string> arguments = new();
        const string expectedError = "ShellCrafter_Test_Error_Output"; // Unique error string

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            executable = "cmd";
            arguments.Add("/c");
            // Redirect stdout (1) to stderr (2) for the echo command
            arguments.Add($"echo {expectedError} 1>&2");
        }
        else // Assume Linux/macOS or other POSIX-like shells
        {
            executable = "sh";
            arguments.Add("-c");
            // Redirect stdout (1) to stderr (2) after the echo command
            arguments.Add($"echo {expectedError} 1>&2");
        }

        // Act: Execute the command
        var result = await ShellCrafter
            .Command(executable)
            .WithArguments(arguments.ToArray())
            .ExecuteAsync();

        // Assert
        Check.That(result.ExitCode).IsEqualTo(0); // Command itself should succeed
        Check.That(result.StandardOutput).IsEmpty(); // Stdout should be empty
        Check.That(result.StandardError).IsEqualTo(expectedError); // Stderr should contain the message
    }

    [Spec]
    public async Task Should_execute_command_in_specified_working_directory()
    {
        // Arrange: Create a unique temp directory for this test
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"ShellCrafterTest_{Guid.NewGuid()}");
        string fullPath = Path.GetFullPath(tempDirectory); // Get canonical full path
        Directory.CreateDirectory(fullPath);

        string executable;
        List<string> arguments = new();
        string expectedOutput = fullPath; // We expect the full path as output

        try // Ensure cleanup happens even if asserts fail
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                executable = "cmd";
                arguments.Add("/c");
                arguments.Add("cd"); // 'cd' command prints the current directory on Windows
            }
            else // Assume Linux/macOS
            {
                executable = "sh";
                arguments.Add("-c");
                arguments.Add("pwd"); // 'pwd' prints the working directory
                                      // Linux pwd output might have a trailing newline, Trim() should handle it
            }

            // Act: Use the *new* fluent method (which doesn't fully work yet)
            var result = await ShellCrafter
                .Command(executable)
                .InWorkingDirectory(fullPath) // <-- The new method call!
                .WithArguments(arguments.ToArray())
                .ExecuteAsync();

            // Assert
            Check.That(result.ExitCode).IsEqualTo(0);
            // On Windows, 'cd' might output an extra newline, Trim() handles this.
            // On Linux, 'pwd' outputs a newline, Trim() handles this.
            Check.That(result.StandardOutput).IsEqualTo(expectedOutput);
            Check.That(result.StandardError).IsEmpty();
        }
        finally
        {
            // Cleanup: Delete the temporary directory
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true); // true for recursive delete
            }
        }
    }

    [Spec]
    public async Task Should_return_non_zero_exit_code_for_failing_command()
    {
        // Arrange
        const int expectedExitCode = 99; // Choose a specific non-zero code
        string executable;
        List<string> arguments = new();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            executable = "cmd";
            arguments.Add("/c");
            // "exit /b" sets errorlevel and exits the cmd instance
            arguments.Add($"exit /b {expectedExitCode}");
        }
        else // Assume Linux/macOS
        {
            executable = "sh";
            arguments.Add("-c");
            // "exit" command exits the shell with the given code
            arguments.Add($"exit {expectedExitCode}");
        }

        // Act: Execute the command that's designed to fail
        var result = await ShellCrafter
            .Command(executable)
            .WithArguments(arguments.ToArray())
            .ExecuteAsync();

        // Assert
        // Verify that the ExitCode matches the specific non-zero code
        Check.That(result.ExitCode).IsEqualTo(expectedExitCode);
        // Depending on the command, stdout/stderr might be empty or contain info
        // For 'exit', they are likely empty. We could assert that if needed.
        // Check.That(result.StandardOutput).IsEmpty();
        // Check.That(result.StandardError).IsEmpty(); 
    }

    [Spec]
    public async Task Should_execute_command_with_specified_environment_variable()
    {
        // Arrange
        const string variableName = "SHELLCRAFTER_SPECIAL_VAR";
        const string expectedValue = "ShellCrafter_Test_Value_ABC"; // A unique value

        string executable;
        List<string> arguments = new();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            executable = "cmd";
            arguments.Add("/c");
            // Use %VAR_NAME% to access environment variable in cmd
            arguments.Add($"echo %{variableName}%");
        }
        else // Assume Linux/macOS
        {
            executable = "sh";
            arguments.Add("-c");
            // Use $VAR_NAME to access environment variable in sh
            // Wrap command in single quotes for sh -c, use double quotes inside
            // for variable expansion if variable contained spaces (though ours doesn't).
            // Safer: 'echo "$VAR_NAME"'
            arguments.Add($"echo ${variableName}"); // Simple case ok here
        }

        // Act: Use the *new* fluent method (which doesn't fully work yet)
        var result = await ShellCrafter
            .Command(executable)
            .WithEnvironmentVariable(variableName, expectedValue) // <-- The new method call!
            .WithArguments(arguments.ToArray())
            .ExecuteAsync();

        // Assert
        Check.That(result.ExitCode).IsEqualTo(0);
        // Output should be the value we set for the environment variable
        Check.That(result.StandardOutput).IsEqualTo(expectedValue);
        Check.That(result.StandardError).IsEmpty();
    }

    [Spec]
    public async Task Should_redirect_string_to_standard_input_correctly()
    {
        // Arrange
        // Use Environment.NewLine for cross-platform line endings in the input
        string inputData = $"InputLine1{Environment.NewLine}InputLine2";
        string expectedOutput = inputData; // Expect the same data back

        string executable;
        List<string> arguments = new();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // PowerShell is generally available on modern Windows & handles stdin well
            executable = "powershell";
            arguments.Add("-Command");
            // $input is a PS automatic variable that enumerates stdin lines
            // Convert to string array, join with newline char from PS perspective (`n)
            // Alternatively, simply rely on default Out-String behaviour
            arguments.Add("$input | Out-String");
        }
        else // Assume Linux/macOS
        {
            executable = "cat"; // 'cat' reads stdin and prints it to stdout
            // No arguments needed for basic 'cat' usage with stdin
        }

        // Act: Use the *new* fluent method (which doesn't fully work yet)
        var result = await ShellCrafter
            .Command(executable)
            .WithArguments(arguments.ToArray()) // Pass arguments for PowerShell/etc.
            .WithStandardInput(inputData)       // <-- The new method call!
            .ExecuteAsync();

        // Assert
        Check.That(result.ExitCode).IsEqualTo(0);
        // StandardOutput should match the input data.
        // Our Trim() in ExecuteAsync should handle the final trailing newline from cat/powershell.
        Check.That(result.StandardOutput).IsEqualTo(expectedOutput);
        Check.That(result.StandardError).IsEmpty();
    }

    [Spec]
    public async Task Should_execute_command_with_multiple_environment_variables()
    {
        // Arrange
        const string varA = "SHELLCRAFTER_MULTI_A";
        const string valA = "Value_A";
        const string varB = "SHELLCRAFTER_MULTI_B";
        const string valB = "Value_B";

        var variablesToAdd = new Dictionary<string, string?>
        {
            [varA] = valA,
            [varB] = valB
            // We could add a null value test later if desired
            // ["SHELLCRAFTER_MULTI_C"] = null 
        };

        // Expect output like "Value_A_Value_B"
        string expectedOutput = $"{valA}_{valB}";

        string executable;
        List<string> arguments = new();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            executable = "cmd";
            arguments.Add("/c");
            // Echo vars separated by underscore
            arguments.Add($"echo %{varA}%_%{varB}%");
        }
        else // Assume Linux/macOS
        {
            executable = "sh";
            arguments.Add("-c");
            // Use quotes for robustness, separate vars with underscore
            arguments.Add($"echo \"${varA}\"_\"${varB}\"");
        }

        // Act: Use the *new* overload method (which doesn't fully work yet)
        var result = await ShellCrafter
            .Command(executable)
            .WithEnvironmentVariables(variablesToAdd) // <-- The new overload method call!
            .WithArguments(arguments.ToArray())
            .ExecuteAsync();

        // Assert
        Check.That(result.ExitCode).IsEqualTo(0);
        Check.That(result.StandardOutput).IsEqualTo(expectedOutput);
        Check.That(result.StandardError).IsEmpty();
    }

    [Spec]
    public async Task Should_kill_process_when_cancelled_if_requested()
    {
        // Arrange
        const int commandDurationSeconds = 5; // How long the command would normally run
        const int cancelAfterMilliseconds = 1000; // Cancel after 1 second
        const int maxWaitMilliseconds = 2000; // Max time test should wait after cancel

        string executable;
        List<string> arguments = new();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // executable = "timeout"; // Old command
            // arguments.Add("/t");
            // arguments.Add(commandDurationSeconds.ToString());
            // arguments.Add("/nobreak");
            executable = "powershell"; // New command
            arguments.Add("-Command");
            arguments.Add($"Start-Sleep -Seconds {commandDurationSeconds}");
        }
        else // Assume Linux/macOS
        {
            executable = "sleep";
            arguments.Add(commandDurationSeconds.ToString());
        }

        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        var stopwatch = new Stopwatch();

        // Act & Assert
        try
        {
            var command = ShellCrafter
                .Command(executable)
                .WithArguments(arguments.ToArray())
                .ExecuteAsync(token, killMode: KillMode.RootProcess); // <-- Pass token and killOnCancel: true

            stopwatch.Start();
            // Schedule cancellation
            cts.CancelAfter(cancelAfterMilliseconds);

            await command; // Expecting this to throw OperationCanceledException

            // If it *doesn't* throw, the test fails (should have been cancelled)
            Assert.Fail("OperationCanceledException was expected but not thrown.");
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            // Assert: Check that cancellation was requested
            Check.That(token.IsCancellationRequested).IsTrue();
            // Assert: Check that it cancelled quickly (implying process was killed)
            Check.That(stopwatch.ElapsedMilliseconds)
                 .IsStrictlyLessThan(maxWaitMilliseconds);
            Console.WriteLine($"Cancelled and stopped in {stopwatch.ElapsedMilliseconds} ms (expected < {maxWaitMilliseconds} ms).");
        }
        catch (Exception ex)
        {
            Assert.Fail($"Unexpected exception type thrown: {ex.GetType().Name}");
        }
        finally
        {
            // Ensure CTS is disposed even if test fails unexpectedly before the using statement ends naturally
            cts?.Dispose();
        }
    }

    [Spec]
    public async Task Should_report_progress_updates_correctly()
    {
        // Arrange
        const string stdOut1 = "StdOut--Line1";
        const string stdErr1 = "StdErr--Line1";
        const string stdOut2 = "StdOut--Line2";

        string executable;
        List<string> arguments = new();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            executable = "cmd";
            arguments.Add("/c");
            // Chain commands using '&'. Need echo for stderr redirection.
            arguments.Add($"echo {stdOut1} & echo {stdErr1} 1>&2 & echo {stdOut2}");
        }
        else // Assume Linux/macOS
        {
            executable = "sh";
            arguments.Add("-c");
            // Chain commands using ';'. Need echo for stderr redirection.
            arguments.Add($"echo {stdOut1} ; echo {stdErr1} 1>&2 ; echo {stdOut2}");
        }

        var progressCollector = new StatusUpdateCollector();

        // Act: Use the *new* fluent method (which doesn't fully work yet)
        var finalResult = await ShellCrafter
            .Command(executable)
            .WithArguments(arguments.ToArray())
            .WithProgress(progressCollector) // <-- The new method call!
            .ExecuteAsync();

        // Assert
        Check.That(finalResult.ExitCode).IsEqualTo(0); // Verify command ran ok

        var updates = progressCollector.Updates;
        Check.That(updates).HasSize(5); // Still expect 5 total updates

        // 1. Check the first update (ProcessStarted)
        Check.That(updates[0]).IsInstanceOf<ProcessStarted>();
        Check.That(((ProcessStarted)updates[0]).ProcessId).IsStrictlyGreaterThan(0);

        // 2. Check the last update (ProcessExited) - Use index ^1 for last item
        Check.That(updates[^1]).IsInstanceOf<ProcessExited>(); // Use ^1 index for last
        var exitedUpdate = (ProcessExited)updates[^1];
        Check.That(exitedUpdate.Result).IsEqualTo(finalResult);
        // Check combined output/error within the final result (as originally tested)
        Check.That(exitedUpdate.Result.StandardOutput).IsEqualTo($"{stdOut1}{Environment.NewLine}{stdOut2}");
        Check.That(exitedUpdate.Result.StandardError).IsEqualTo(stdErr1);

        // 3. Check the middle three updates for content, regardless of order
        // Extract the updates between the first and the last
        var middleUpdates = updates.Skip(1).Take(updates.Count - 2).ToList();
        Check.That(middleUpdates).HasSize(3); // Ensure we got 3 middle items

        // Check that there's exactly one StdErr update with the correct data
        Check.That(middleUpdates.OfType<StdErrDataReceived>()).HasSize(1);
        Check.That(middleUpdates.OfType<StdErrDataReceived>().Single().Data).IsEqualTo(stdErr1);

        // Check that there are exactly two StdOut updates with the correct data (in any order)
        Check.That(middleUpdates.OfType<StdOutDataReceived>()).HasSize(2);
        Check.That(middleUpdates.OfType<StdOutDataReceived>().Select(u => u.Data)) // Select the strings
            .IsEquivalentTo(new[] { stdOut1, stdOut2 }); // Check if content is equivalent to the expected array, regardless of order
    }

    [Spec]
    public async Task Should_throw_TimeoutException_when_timeout_is_exceeded()
    {
        // Arrange
        const int commandDurationSeconds = 5;
        var timeoutDuration = TimeSpan.FromSeconds(1); // Timeout shorter than command
        const int maxWaitMilliseconds = 2000; // Max time test should wait 

        string executable;
        List<string> arguments = new();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            executable = "powershell";
            arguments.Add("-Command");
            arguments.Add($"Start-Sleep -Seconds {commandDurationSeconds}");
        }
        else // Assume Linux/macOS
        {
            executable = "sleep";
            arguments.Add(commandDurationSeconds.ToString());
        }

        var stopwatch = new Stopwatch();
        var commandBuilder = ShellCrafter
            .Command(executable)
            .WithArguments(arguments.ToArray())
            .WithTimeout(timeoutDuration); // <-- Use the new method

        // Act & Assert
        stopwatch.Start();
        // Use Check.ThatCode (as Check.ThatAsyncCode is obsolete in this version)
        Check.ThatCode(async () =>
        {
            await commandBuilder.ExecuteAsync(killMode: KillMode.RootProcess);
        })
            .Throws<TimeoutException>(); // Asserts the expected exception

        // This line is reached only if Throws<T> passes
        stopwatch.Stop();

        // Assert on duration
        Check.That(stopwatch.ElapsedMilliseconds).IsStrictlyLessThan(maxWaitMilliseconds);
        Console.WriteLine($"Threw TimeoutException after {stopwatch.ElapsedMilliseconds} ms (expected < {maxWaitMilliseconds} ms).");
    }

    [Spec] // Or rename if you duplicated
    public async Task Should_attempt_kill_process_tree_when_cancelled() // Renamed test
    {
        // Arrange - Identical to the root process kill test
        const int commandDurationSeconds = 5;
        var timeoutDuration = TimeSpan.FromSeconds(1);
        const int maxWaitMilliseconds = 2000;

        string executable; List<string> arguments = new(); // ... setup sleep/powershell command ...

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            executable = "powershell";
            arguments.Add("-Command");
            arguments.Add($"Start-Sleep -Seconds {commandDurationSeconds}");
        }
        else // Assume Linux/macOS
        {
            executable = "sleep";
            arguments.Add(commandDurationSeconds.ToString());
        }

        using var cts = new CancellationTokenSource();
        var stopwatch = new Stopwatch();
        var commandBuilder = ShellCrafter
            .Command(executable)
            .WithArguments(arguments.ToArray())
            // Include timeout to trigger cancellation easily for the test
            .WithTimeout(timeoutDuration);

        // Act & Assert
        stopwatch.Start();
        // Use Check.ThatCode (assuming it now compiles)
        Check.ThatCode(async () =>
        {
            // vvv Use KillMode.ProcessTree vvv
            await commandBuilder.ExecuteAsync(cancellationToken: cts.Token, killMode: KillMode.ProcessTree);
        })
            .Throws<TimeoutException>(); // Still expect TimeoutException due to WithTimeout
        stopwatch.Stop();

        // Assert on duration - ensures kill attempt happened quickly
        Check.That(stopwatch.ElapsedMilliseconds).IsStrictlyLessThan(maxWaitMilliseconds);
        Console.WriteLine($"Process tree kill attempted, threw TimeoutException after {stopwatch.ElapsedMilliseconds} ms (expected < {maxWaitMilliseconds} ms).");
    }

    [Spec]
    public async Task Should_redirect_stream_to_standard_input_correctly()
    {
        // Arrange
        string inputData = $"StreamLine1{Environment.NewLine}StreamLine2";
        string expectedOutput = inputData;
        byte[] inputBytes = Encoding.UTF8.GetBytes(inputData); // Use UTF8 encoding

        // Create a MemoryStream from the input string bytes
        using var inputStream = new MemoryStream(inputBytes); // 'using' ensures disposal

        string executable;
        List<string> arguments = new();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            executable = "powershell";
            arguments.Add("-Command");
            arguments.Add("$input | Out-String");
        }
        else // Assume Linux/macOS
        {
            executable = "cat";
        }

        var result = await ShellCrafter
            .Command(executable)
            .WithArguments(arguments.ToArray())
            .WithStandardInput(inputStream)       // <-- The new stream overload call!
            .WithTimeout(TimeSpan.FromSeconds(15))
            .ExecuteAsync();

        // Assert
        Check.That(result.ExitCode).IsEqualTo(0);
        Check.That(result.StandardOutput).IsEqualTo(expectedOutput);
        Check.That(result.StandardError).IsEmpty();
    }
}
