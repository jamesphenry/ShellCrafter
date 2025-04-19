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
    private readonly Dictionary<string, string?> _environmentVariables = new();
    private string? _standardInput = null;
    private IProgress<StatusUpdate>? _progressHandler = null;


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
    public async Task<ExecutionResult> ExecuteAsync(CancellationToken cancellationToken = default, bool killOnCancel = false) // Allow cancellation token passing
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = _executable,
            // Properly quote arguments if they contain spaces (basic handling)
            Arguments = string.Join(" ", _arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false, 
            CreateNoWindow = true,  // Don't pop up a window
            WorkingDirectory = _workingDirectory, // Set working directory if specified
            StandardOutputEncoding = Encoding.UTF8, 
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (_environmentVariables.Count > 0)
        {
            foreach (var kvp in _environmentVariables)
            {
                processStartInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }

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

        process.OutputDataReceived += (sender, e) =>
            // Call the extension method, providing a lambda to create StdOutDataReceived
            e.HandleDataReceived(
                stdOutputBuilder,      // The builder for stdout
                _progressHandler,      // The progress handler
                outputCloseEvent,      // The TCS for stdout closing
                data => new StdOutDataReceived(data) // Lambda to create the correct status update
            );

        process.ErrorDataReceived += (sender, e) =>
            // Call the extension method, providing a lambda to create StdErrDataReceived
            e.HandleDataReceived(
                stdErrorBuilder,       // The builder for stderr
                _progressHandler,      // The progress handler
                errorCloseEvent,       // The TCS for stderr closing
                data => new StdErrDataReceived(data) // Lambda to create the correct status update
            );

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

        // Report Process Started right after successful start
        _progressHandler?.Report(new ProcessStarted(process.Id));

        // Begin reading streams asynchronously
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!string.IsNullOrEmpty(_standardInput))
        {
            // Get the input stream writer. Using 'using' ensures it's disposed
            // and the stream is closed, signaling end-of-input to the child process.
            using (StreamWriter standardInputWriter = process.StandardInput)
            {
                // Ensure writer doesn't auto-flush prematurely if not needed,
                // though for simple WriteAsync it's usually fine.
                // standardInputWriter.AutoFlush = false; 

                // Write the input data asynchronously
                await standardInputWriter.WriteAsync(_standardInput);
                // Explicitly close/dispose happens with 'using' block exit
            }
        }

        ExecutionResult finalResult;

        try 
        {
            // Wait for the process to exit OR cancellation
            await process.WaitForExitAsync(cancellationToken);

            // Wait for the stream reading tasks to complete ONLY if exit wasn't cancelled
            await Task.WhenAll(outputCloseEvent.Task, errorCloseEvent.Task);

        }
        catch (OperationCanceledException) // vvv Add catch block vvv
        {
            if (killOnCancel && !process.HasExited) // Check if already exited naturally
            {
                try
                {
                    process.Kill();
                    // Consider process.Kill(true) on .NET 5+ to kill child processes too.
                    // This might need different configuration/handling.
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is NotSupportedException)
                {
                }
            }
            throw;
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(outputCloseEvent.Task, errorCloseEvent.Task);

            // If no cancellation, create the result normally
            int exitCode = process.ExitCode;
            finalResult = new ExecutionResult(
                exitCode,
                stdOutputBuilder.ToString().Trim(),
                stdErrorBuilder.ToString().Trim()
            );
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"Cancellation requested... killOnCancel={killOnCancel}");
            if (killOnCancel && !process.HasExited)
            {
                try { process.Kill(); } catch (Exception ex) { Console.WriteLine($"Failed to kill: {ex.Message}"); }
            }
            // What should the 'finalResult' be on cancellation? 
            // The 'Exited' event might not be meaningful, or we could report a specific state.
            // For now, let's *not* report ProcessExited if cancelled, just rethrow.
            throw;
        }

        // Report Process Exited AFTER successful completion and creating the result
        _progressHandler?.Report(new ProcessExited(finalResult)); // <<< Report Exited

        return finalResult; // Return the result
    }


    public CommandBuilder InWorkingDirectory(string path)
    {
        // Basic validation - check if directory exists? Or let ProcessStartInfo handle it?
        // Let's defer validation for now, ProcessStartInfo will error if invalid.
        _workingDirectory = path;
        return this; // Return 'this' for chaining
    }

    public CommandBuilder WithEnvironmentVariable(string key, string? value) // Allow null to unset/clear maybe?
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("Environment variable key cannot be null or empty.", nameof(key));
        }
        _environmentVariables[key] = value; // Add or overwrite
        return this;
    }



    public CommandBuilder WithStandardInput(string input)
    {
        _standardInput = input ?? throw new ArgumentNullException(nameof(input)); // Basic check
        return this;
    }

    public CommandBuilder WithEnvironmentVariables(IDictionary<string, string?> variables)
    {
        if (variables == null)
        {
            throw new ArgumentNullException(nameof(variables));
        }

        foreach (var kvp in variables)
        {
            // Reuse validation from single method or inline check
            if (string.IsNullOrEmpty(kvp.Key))
            {
                // Or collect errors and throw aggregate? For now, fail fast.
                throw new ArgumentException("Environment variable keys in the dictionary cannot be null or empty.", nameof(variables));
            }
            // Add or overwrite in the internal dictionary
            _environmentVariables[kvp.Key] = kvp.Value;
        }

        return this;
    }

    public CommandBuilder WithProgress(IProgress<StatusUpdate> progress)
    {
        _progressHandler = progress ?? throw new ArgumentNullException(nameof(progress));
        return this;
    }
}