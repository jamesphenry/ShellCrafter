// ShellCrafter.Tests/Spec.cs
namespace ShellCrafter.Tests; // Use your test project's namespace

using Xunit; // Make sure you have the 'using Xunit;' statement
using System.Runtime.CompilerServices;

public class Spec : FactAttribute
{
    public Spec([CallerMemberName] string testMethodName = "")
    {
        // Ensure null or empty names don't cause issues
        if (!string.IsNullOrEmpty(testMethodName))
        {
            DisplayName = testMethodName.Replace("_", " ");
        }
    }
}