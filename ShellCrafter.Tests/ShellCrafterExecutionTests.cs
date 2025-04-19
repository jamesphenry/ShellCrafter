// ShellCrafter.Tests/ShellCrafterExecutionTests.cs
namespace ShellCrafter.Tests;

using Xunit;
using NFluent;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using System.Collections.Generic;
using System.Text;

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
}