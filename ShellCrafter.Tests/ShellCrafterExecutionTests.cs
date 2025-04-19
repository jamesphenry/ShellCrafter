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
}
