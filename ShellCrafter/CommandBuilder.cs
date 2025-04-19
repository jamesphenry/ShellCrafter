// ShellCrafter/CommandBuilder.cs
namespace ShellCrafter;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics; 
using System.Text; 

public class CommandBuilder
{
    private readonly string _executable;
    private readonly List<string> _arguments = new();
    private string? _workingDirectory = null;

    internal CommandBuilder(string executable)
    {
        _executable = executable ?? throw new ArgumentNullException(nameof(executable));
    }

    public CommandBuilder WithArguments(params string[] args)
    {
        _arguments.AddRange(args);
        return this;
    }

    // Make the method async
    public async Task<ExecutionResult> ExecuteAsync(CancellationToken cancellationToken = default) // Allow cancellation token passing
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = _executable,
            // Properly quote arguments if they contain spaces (basic handling)
            Arguments = string.Join(" ", _arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false, // Required for stream redirection
            CreateNoWindow = true,  // Don't pop up a window
            WorkingDirectory = _workingDirectory, // Set working directory if specified
            // Consider setting encoding if needed:
            // StandardOutputEncoding = Encoding.UTF8, 
            // StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = processStartInfo };

        if (process == null)
        {
            // Should not happen if allocation succeeds, but defensive check
            throw new InvalidOperationException($"Failed to create process for {_executable}.");
        }

        var stdOutputBuilder = new StringBuilder();
        var stdErrorBuilder = new StringBuilder();

        // Use TaskCompletionSource to signal when reading is done
        var outputCloseEvent = new TaskCompletionSource<bool>();
        var errorCloseEvent = new TaskCompletionSource<bool>();

        process.OutputDataReceived += (sender, e) => {
            if (e.Data == null)
            {
                outputCloseEvent.TrySetResult(true); // Signal that stream is closed
            }
            else
            {
                stdOutputBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) => {
            if (e.Data == null)
            {
                errorCloseEvent.TrySetResult(true); // Signal that stream is closed
            }
            else
            {
                stdErrorBuilder.AppendLine(e.Data);
            }
        };

        bool isStarted;
        try
        {
            isStarted = process.Start();
        }
        catch (Exception ex)
        {
            // Provide more context on failure
            throw new InvalidOperationException($"Failed to start process '{_executable}'. Verify the executable path and permissions.", ex);
        }

        if (!isStarted)
        {
            // Process failed to start, maybe invalid executable?
            return new ExecutionResult(-1, string.Empty, $"Process failed to start for executable: {_executable}");
        }

        // Begin reading streams asynchronously
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait for the process to exit OR cancellation
        await process.WaitForExitAsync(cancellationToken);

        // Wait for the stream reading tasks to complete
        await Task.WhenAll(outputCloseEvent.Task, errorCloseEvent.Task);

        // Check exit code AFTER WaitForExit/WaitForExitAsync
        int exitCode = process.ExitCode;

        // Return the actual results
        return new ExecutionResult(
            exitCode,
            stdOutputBuilder.ToString().Trim(), // Trim trailing newline
            stdErrorBuilder.ToString().Trim()   // Trim trailing newline
            );
    }

    public CommandBuilder InWorkingDirectory(string path)
    {
        // Basic validation - check if directory exists? Or let ProcessStartInfo handle it?
        // Let's defer validation for now, ProcessStartInfo will error if invalid.
        _workingDirectory = path;
        return this; // Return 'this' for chaining
    }
}