// ShellCrafter/ShellCrafter.cs
namespace ShellCrafter;

using System;
using System.Data.Common;
using System.Threading.Tasks; // Add this

public static class ShellCrafter
{
    public static CommandBuilder Command(string executable)
    {
        // We return a new builder instance here, passing the executable.
        // It won't do much yet, but it's a start.
        return new CommandBuilder(executable);
    }
}