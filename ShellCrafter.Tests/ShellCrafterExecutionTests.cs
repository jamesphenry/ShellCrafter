// ShellCrafter.Tests/ShellCrafterExecutionTests.cs
namespace ShellCrafter.Tests;

using Xunit;
using NFluent;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

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
}
