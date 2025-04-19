// ShellCrafter.Tests/ShellCrafterExecutionTests.cs
namespace ShellCrafter.Tests;

using Xunit;
using NFluent;
using System.Threading.Tasks;

public class ShellCrafterExecutionTests
{
    [Spec]
    public async Task ShouldExecuteBasicCommandSuccessfully()
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
}