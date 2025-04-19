// ShellCrafter/CommandBuilder.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ShellCrafter;

public class CommandBuilder
{
    // --- Fields ---
    private readonly string _executable;
    private readonly List<string> _arguments = new();
    private readonly Dictionary<string, string?> _environmentVariables = new();
    private string? _workingDirectory = null;
    private string? _standardInput = null;
    private Stream? _standardInputStream = null;
    private IProgress<StatusUpdate>? _progressHandler = null;
    private TimeSpan? _timeout = null;


    // --- Constructor ---
    internal CommandBuilder(string executable)
    {
        _executable = executable ?? throw new ArgumentNullException(nameof(executable));
    }

    // --- Fluent Configuration Methods ---
    public CommandBuilder WithArguments(params string[] args)
    {
        _arguments.AddRange(args);
        return this;
    }

    public CommandBuilder InWorkingDirectory(string path)
    {
        _workingDirectory = path;
        return this;
    }

    public CommandBuilder WithEnvironmentVariable(string key, string? value)
    {
        if (string.IsNullOrEmpty(key)) { throw new ArgumentException("...", nameof(key)); }
        _environmentVariables[key] = value;
        return this;
    }

    public CommandBuilder WithEnvironmentVariables(IDictionary<string, string?> variables)
    {
        if (variables == null) { throw new ArgumentNullException(nameof(variables)); }
        foreach (var kvp in variables)
        {
            if (string.IsNullOrEmpty(kvp.Key)) { throw new ArgumentException("...", nameof(variables)); }
            _environmentVariables[kvp.Key] = kvp.Value;
        }
        return this;
    }

    public CommandBuilder WithStandardInput(string input)
    {
        _standardInput = input ?? throw new ArgumentNullException(nameof(input));
        return this;
    }

    public CommandBuilder WithStandardInput(Stream inputStream)
    {
        _standardInputStream = inputStream ?? throw new ArgumentNullException(nameof(inputStream));
        // Optionally check inputStream.CanRead here? Maybe defer to ExecuteAsync.
        _standardInput = null; // Clear string input if stream is set
        return this;
    }

    public CommandBuilder WithTimeout(TimeSpan duration)
    {
        // Basic validation: Timeout should be positive. 
        // Timeout.InfiniteTimeSpan is also valid (-1ms). 0 is often problematic.
        if (duration <= TimeSpan.Zero && duration != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Timeout duration must be positive or Timeout.InfiniteTimeSpan.");
        }
        _timeout = duration;
        return this;
    }

    public CommandBuilder WithProgress(IProgress<StatusUpdate> progress)
    {
        _progressHandler = progress ?? throw new ArgumentNullException(nameof(progress));
        return this;
    }

    // --- Execution Orchestrator ---
    // Corrected ExecuteAsync in CommandBuilder.cs
    public async Task<ExecutionResult> ExecuteAsync(
        CancellationToken cancellationToken = default, // External token
        KillMode killMode = KillMode.NoKill)
    {
        var processStartInfo = ConfigureProcessStartInfo();

        using var process = new Process { StartInfo = processStartInfo };
        if (process == null) { throw new InvalidOperationException($"Failed to create process for {_executable}."); }

        var stdOutputBuilder = new StringBuilder();
        var stdErrorBuilder = new StringBuilder();
        var outputCloseEvent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorCloseEvent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        AttachEventHandlers(process, stdOutputBuilder, stdErrorBuilder, outputCloseEvent, errorCloseEvent);

        // --- Timeout and Cancellation Handling Setup ---
        CancellationTokenSource? internalTimeoutCts = null;
        CancellationTokenSource? linkedCts = null;
        CancellationToken effectiveToken = cancellationToken; // Start with external token

        // Check if timeout is configured by the user
        if (_timeout.HasValue && _timeout.Value != Timeout.InfiniteTimeSpan)
        {
            internalTimeoutCts = new CancellationTokenSource();
            try
            {
                internalTimeoutCts.CancelAfter(_timeout.Value); // Set timeout
            }
            catch (ArgumentOutOfRangeException ex) // Handle potential issue if _timeout is somehow invalid despite earlier check
            {
                internalTimeoutCts.Dispose(); // Dispose if CancelAfter fails
                throw new ArgumentOutOfRangeException(nameof(_timeout), $"Invalid timeout value provided: {_timeout.Value}", ex.Message);
            }

            // Link internal timeout token with the external token
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, internalTimeoutCts.Token);
            effectiveToken = linkedCts.Token; // Use the linked token for waiting
        }
        // --- End Timeout Setup ---

        ExecutionResult? finalResult = null; // Declare here for use after try block

        try // Wrap main execution in try/finally to ensure CTS disposal
        {
            StartProcess(process);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await WriteStandardInputAsync(process);

            // Handles wait, cancellation, kill, and waits for streams
            // Pass the effectiveToken and the specific internalTimeoutCts
            await HandleProcessExitAsync(process, killMode, outputCloseEvent.Task, errorCloseEvent.Task, effectiveToken, internalTimeoutCts);

            // If HandleProcessExitAsync didn't throw (i.e., no cancellation), create result
            finalResult = CreateFinalResult(process, stdOutputBuilder, stdErrorBuilder);

            // Report exit and return (Only report if not cancelled)
            _progressHandler?.Report(new ProcessExited(finalResult)); // Use .Value as finalResult is nullable
            return finalResult;
        }
        // No catch here - HandleProcessExitAsync throws/re-throws appropriately
        finally
        {
            // Ensure CTSs are disposed
            internalTimeoutCts?.Dispose();
            linkedCts?.Dispose();
        }
    }

    // --- Private Helper Methods ---

    private ProcessStartInfo ConfigureProcessStartInfo()
    {
        var psi = new ProcessStartInfo
        {
            FileName = _executable,
            Arguments = string.Join(" ", _arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true, // Always true now, WriteStandardInputAsync checks if data exists
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _workingDirectory
            // StandardInputEncoding = Encoding.UTF8, // Consider making configurable?
            // StandardOutputEncoding = Encoding.UTF8,
            // StandardErrorEncoding = Encoding.UTF8,
        };

        if (_environmentVariables.Count > 0)
        {
            foreach (var kvp in _environmentVariables) { psi.EnvironmentVariables[kvp.Key] = kvp.Value; }
        }
        return psi;
    }

    private void AttachEventHandlers(Process process, StringBuilder stdOutBuilder, StringBuilder stdErrBuilder, TaskCompletionSource<bool> outputTcs, TaskCompletionSource<bool> errorTcs)
    {
        process.OutputDataReceived += (sender, e) =>
            e.HandleDataReceived(stdOutBuilder, _progressHandler, outputTcs, data => new StdOutDataReceived(data));

        process.ErrorDataReceived += (sender, e) =>
            e.HandleDataReceived(stdErrBuilder, _progressHandler, errorTcs, data => new StdErrDataReceived(data));
    }

    private void StartProcess(Process process)
    {
        bool isStarted;
        try
        {
            isStarted = process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start process '{_executable}'. Verify path/permissions.", ex);
        }

        if (!isStarted)
        {
            // Should not happen if Start() returns false without exception, but good practice
            throw new InvalidOperationException($"Process failed to start for executable: {_executable}");
        }
        _progressHandler?.Report(new ProcessStarted(process.Id));
    }

    private async Task WriteStandardInputAsync(Process process)
    {
        // Prioritize Stream input if provided
        if (_standardInputStream != null) // <<< Check stream field first
        {
            if (!_standardInputStream.CanRead)
            {
                // Or handle differently? For now, throw if unreadable.
                throw new InvalidOperationException("Provided standard input stream is not readable.");
            }

            // Get the process's input stream writer. 
            // IMPORTANT: Using 'using' ensures Dispose/Close is called, signaling EOF to the process.
            using (var standardInputWriter = process.StandardInput)
            {
                // Asynchronously copy from the user's stream to the process's input stream
                // This will read _standardInputStream until it ends.
                await _standardInputStream.CopyToAsync(standardInputWriter.BaseStream);

                // No need to explicitly FlushAsync or Close, CopyToAsync handles awaits
                // and the 'using' block handles disposal/closing the process stream writer.
            }
        }
        // Fall back to string input if stream wasn't provided
        else if (!string.IsNullOrEmpty(_standardInput))
        {
            if (process.StandardInput == null)
            {
                throw new InvalidOperationException("Standard input stream is null. Cannot write input.");
            }
            using (StreamWriter standardInputWriter = process.StandardInput)
            {
                // Existing logic for writing string
                await standardInputWriter.WriteAsync(_standardInput);
            }
        }
        // If neither _standardInputStream nor _standardInput is set, do nothing.
    }

    private async Task HandleProcessExitAsync(Process process, KillMode killMode, Task outputCompletionTask, Task errorCompletionTask, CancellationToken effectiveToken, CancellationTokenSource? internalTimeoutCts) // <<< Use KillMode
    {
        try
        {
            await process.WaitForExitAsync(effectiveToken);
            await Task.WhenAll(outputCompletionTask, errorCompletionTask);
            //Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Process {process.Id}: Wait completed NORMALLY."); // Debug
        }
        catch (OperationCanceledException ex)
        {
            // Determine if the cancellation was triggered by our internal timeout CTS.
            // The ?? false handles the case where no timeout was set (_internalTimeoutCts is null).
            bool timeoutOccurred = internalTimeoutCts?.IsCancellationRequested ?? false;

            if (timeoutOccurred)
            {
                // --- Timeout Occurred ---
                if (timeoutOccurred)
                {
                    if (killMode != KillMode.NoKill) // Check if any kill is requested
                    {
                        AttemptKillProcess(process, killMode); // Pass killMode
                    }
                }
                // Throw the specific TimeoutException, wrapping the original OCE
                throw new TimeoutException($"The operation timed out after {_timeout!.Value}.", ex); // Use !.Value as timeoutOccurred is true only if _timeout has value
            }
            else
            {
                // --- External Cancellation Occurred ---
                // If OCE was caught and it wasn't our timeout, it must be the external token.
                if (killMode != KillMode.NoKill) // Check if any kill is requested
                {
                    AttemptKillProcess(process, killMode); // Pass killMode
                }
                // Re-throw the original OCE (likely TaskCanceledException) to signal external cancellation
                throw;
            }
        }
    }

    private void AttemptKillProcess(Process process, KillMode killMode) // Accepts KillMode now
    {
        // Should only be called if killMode is RootProcess or ProcessTree
        if (killMode == KillMode.NoKill) return;

        try
        {
            if (!process.HasExited)
            {
                if (killMode == KillMode.ProcessTree)
                {
#if NET5_0_OR_GREATER // Check if targeting .NET 5 or later
                    Console.WriteLine($"Attempting to kill process tree starting with {process.Id}..."); // Debug/Log
                    process.Kill(true); // Kill the entire process tree
#else
                // Fallback for older frameworks that don't support killing the tree
                 Console.WriteLine($"Attempting to kill root process {process.Id} (Tree kill not supported on this framework)..."); // Debug/Log
                 process.Kill(); 
#endif
                }
                else // killMode == KillMode.RootProcess
                {
                    Console.WriteLine($"Attempting to kill root process {process.Id}..."); // Debug/Log
                    process.Kill(); // Kill just the root process
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is NotSupportedException || ex is System.ComponentModel.Win32Exception)
        {
            Console.WriteLine($"Failed or unnecessary to kill process {process.Id}: {ex.Message}"); // Debug/Log
                                                                                                    // Swallow exception - best effort kill
        }
    }

    private ExecutionResult CreateFinalResult(Process process, StringBuilder stdOutBuilder, StringBuilder stdErrBuilder)
    {
        // Ensure process has exited before getting ExitCode (should be guaranteed by HandleProcessExitAsync)
        int exitCode = process.ExitCode;

        return new ExecutionResult(
            exitCode,
            stdOutBuilder.ToString().Trim(), // Still trim final result
            stdErrBuilder.ToString().Trim()
        );
    }


}