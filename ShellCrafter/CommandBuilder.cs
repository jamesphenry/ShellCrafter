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
    private Stream? _stdOutPipeTarget = null;
    private Stream? _stdErrPipeTarget = null;
    private bool _captureStdOut = true; // Default to capturing internally
    private bool _captureStdErr = true; // Default to capturing internally


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
        if (string.IsNullOrEmpty(key)) { throw new ArgumentException("Environment variable key cannot be null or empty.", nameof(key)); }
        _environmentVariables[key] = value;
        return this;
    }

    public CommandBuilder WithEnvironmentVariables(IDictionary<string, string?> variables)
    {
        if (variables == null) { throw new ArgumentNullException(nameof(variables)); }
        foreach (var kvp in variables)
        {
            if (string.IsNullOrEmpty(kvp.Key)) { throw new ArgumentException("Environment variable keys in the dictionary cannot be null or empty.", nameof(variables)); }
            _environmentVariables[kvp.Key] = kvp.Value;
        }
        return this;
    }

    // Sets string input, clears stream input
    public CommandBuilder WithStandardInput(string input)
    {
        _standardInput = input ?? throw new ArgumentNullException(nameof(input));
        _standardInputStream = null;
        return this;
    }

    // Sets stream input, clears string input
    public CommandBuilder WithStandardInput(Stream inputStream)
    {
        _standardInputStream = inputStream ?? throw new ArgumentNullException(nameof(inputStream));
        _standardInput = null;
        return this;
    }

    public CommandBuilder PipeStandardOutputTo(Stream target, bool captureInternal = false)
    {
        _stdOutPipeTarget = target ?? throw new ArgumentNullException(nameof(target));
        if (!target.CanWrite) throw new ArgumentException("Target stream must be writable.", nameof(target));
        _captureStdOut = captureInternal;
        return this;
    }

    public CommandBuilder PipeStandardErrorTo(Stream target, bool captureInternal = false)
    {
        _stdErrPipeTarget = target ?? throw new ArgumentNullException(nameof(target));
        if (!target.CanWrite) throw new ArgumentException("Target stream must be writable.", nameof(target));
        _captureStdErr = captureInternal;
        return this;
    }

    public CommandBuilder WithProgress(IProgress<StatusUpdate> progress)
    {
        _progressHandler = progress ?? throw new ArgumentNullException(nameof(progress));
        return this;
    }

    public CommandBuilder WithTimeout(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero && duration != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Timeout duration must be positive or Timeout.InfiniteTimeSpan.");
        }
        _timeout = duration;
        return this;
    }

    // --- Execution Orchestrator ---
    public async Task<ExecutionResult> ExecuteAsync(
        CancellationToken cancellationToken = default,
        KillMode killMode = KillMode.NoKill)
    {
        var processStartInfo = ConfigureProcessStartInfo();

        using var process = new Process { StartInfo = processStartInfo };
        if (process == null) { throw new InvalidOperationException($"Failed to create process for {_executable}."); }

        // --- Timeout and Cancellation Handling Setup ---
        CancellationTokenSource? internalTimeoutCts = null;
        CancellationTokenSource? linkedCts = null;
        CancellationToken effectiveToken = cancellationToken;
        if (_timeout.HasValue && _timeout.Value != Timeout.InfiniteTimeSpan)
        {
            internalTimeoutCts = new CancellationTokenSource();
            try { internalTimeoutCts.CancelAfter(_timeout.Value); }
            catch (ArgumentOutOfRangeException ex)
            {
                internalTimeoutCts.Dispose();
                // *** Corrected Exception Constructor ***
                throw new ArgumentOutOfRangeException(nameof(_timeout), $"Invalid timeout value provided: {_timeout.Value}", ex.Message);
            }
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, internalTimeoutCts.Token);
            effectiveToken = linkedCts.Token;
        }
        // --- End Timeout Setup ---

        // --- Output/Error Handling Setup ---
        var stdOutputBuilder = _captureStdOut ? new StringBuilder() : null;
        var stdErrorBuilder = _captureStdErr ? new StringBuilder() : null;
        Task outputCompletionTask = Task.CompletedTask;
        Task errorCompletionTask = Task.CompletedTask;
        TaskCompletionSource<bool>? outputCloseEvent = null; // Null if piping stdout
        TaskCompletionSource<bool>? errorCloseEvent = null;  // Null if piping stderr
        // --- End Output/Error Handling Setup ---

        ExecutionResult finalResult; // *** Removed nullable '?' ***

        try
        {
            StartProcess(process); // Start process, report ProcessStarted

            // --- Handle Standard Output ---
            if (_stdOutPipeTarget != null)
            {
                // Piping: Start CopyToAsync, do not use events/BeginRead
                outputCompletionTask = process.StandardOutput.BaseStream.CopyToAsync(_stdOutPipeTarget, effectiveToken);
            }
            else if (_captureStdOut && stdOutputBuilder != null) // Check builder not null just in case
            {
                // Internal Capture: Use events
                outputCloseEvent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                process.OutputDataReceived += (sender, e) =>
                    e.HandleDataReceived(stdOutputBuilder, _progressHandler, outputCloseEvent, data => new StdOutDataReceived(data));
                process.BeginOutputReadLine();
                outputCompletionTask = outputCloseEvent.Task;
            }

            // --- Handle Standard Error ---
            if (_stdErrPipeTarget != null)
            {
                // Piping: Start CopyToAsync, do not use events/BeginRead
                errorCompletionTask = process.StandardError.BaseStream.CopyToAsync(_stdErrPipeTarget, effectiveToken);
            }
            else if (_captureStdErr && stdErrorBuilder != null) // Check builder not null
            {
                // Internal Capture: Use events
                errorCloseEvent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                process.ErrorDataReceived += (sender, e) =>
                   e.HandleDataReceived(stdErrorBuilder, _progressHandler, errorCloseEvent, data => new StdErrDataReceived(data));
                process.BeginErrorReadLine();
                errorCompletionTask = errorCloseEvent.Task;
            }

            await WriteStandardInputAsync(process); // Write input after starting reads/pipes

            // Handles wait, cancellation, kill, and waits for stream tasks
            await HandleProcessExitAsync(process, killMode, outputCompletionTask, errorCompletionTask, effectiveToken, internalTimeoutCts);

            // If HandleProcessExitAsync didn't throw, create result
            finalResult = CreateFinalResult(process, stdOutputBuilder, stdErrorBuilder);

            // Report exit 
            _progressHandler?.Report(new ProcessExited(finalResult)); // *** Removed .Value ***
            return finalResult; // *** Removed .Value ***
        }
        finally
        {
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
            RedirectStandardOutput = true, // Always needed to access BaseStream or events
            RedirectStandardError = true,  // Always needed
            RedirectStandardInput = true,  // Always needed
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _workingDirectory
        };
        if (_environmentVariables.Count > 0) { /* ... */ } // Assume correct
        return psi;
    }

    // No longer needed - logic moved into ExecuteAsync
    // private void AttachEventHandlers(...) { } 

    private void StartProcess(Process process)
    {
        bool isStarted;
        try { isStarted = process.Start(); }
        catch (Exception ex) { throw new InvalidOperationException($"Failed to start process '{_executable}'. Verify path/permissions.", ex); }
        if (!isStarted) { throw new InvalidOperationException($"Process failed to start for executable: {_executable}"); }
        _progressHandler?.Report(new ProcessStarted(process.Id));
    }

    private async Task WriteStandardInputAsync(Process process)
    {
        if (_standardInputStream != null) // Check stream first
        {
            if (!_standardInputStream.CanRead) { throw new InvalidOperationException("Provided standard input stream is not readable."); }
            using (var standardInputWriter = process.StandardInput) { await _standardInputStream.CopyToAsync(standardInputWriter.BaseStream); }
        }
        else if (!string.IsNullOrEmpty(_standardInput)) // Then check string
        {
            if (process.StandardInput == null) { throw new InvalidOperationException("Standard input stream is null. Cannot write input."); }
            using (StreamWriter standardInputWriter = process.StandardInput) { await standardInputWriter.WriteAsync(_standardInput); }
        }
    }

    private async Task HandleProcessExitAsync(Process process, KillMode killMode, Task outputCompletionTask, Task errorCompletionTask, CancellationToken effectiveToken, CancellationTokenSource? internalTimeoutCts)
    {
        try
        {
            await process.WaitForExitAsync(effectiveToken);
            await Task.WhenAll(outputCompletionTask, errorCompletionTask);
        }
        catch (OperationCanceledException ex)
        {
            bool timeoutOccurred = internalTimeoutCts?.IsCancellationRequested ?? false;
            if (timeoutOccurred)
            {
                // Console.WriteLine($"Timeout exceeded..."); // Keep console logs out of lib code generally
                if (killMode != KillMode.NoKill) { AttemptKillProcess(process, killMode); }
                throw new TimeoutException($"The operation timed out after {_timeout!.Value}.", ex);
            }
            else // External Cancellation
            {
                // Console.WriteLine($"External cancellation...");
                if (killMode != KillMode.NoKill) { AttemptKillProcess(process, killMode); }
                throw;
            }
        }
    }

    private void AttemptKillProcess(Process process, KillMode killMode)
    {
        if (killMode == KillMode.NoKill) return;
        try { if (!process.HasExited) { /* ... Kill/Kill(true) logic ... */ } } // Assume correct
        catch (Exception ex) when (ex is InvalidOperationException || ex is NotSupportedException || ex is System.ComponentModel.Win32Exception) { /* Console.WriteLine($"Failed kill..."); */ } // Swallow
    }

    private ExecutionResult CreateFinalResult(Process process, StringBuilder? stdOutBuilder, StringBuilder? stdErrBuilder) // Accept nullable
    {
        int exitCode = process.ExitCode;
        string stdOutResult = _captureStdOut && stdOutBuilder != null ? stdOutBuilder.ToString().Trim() : string.Empty;
        string stdErrResult = _captureStdErr && stdErrBuilder != null ? stdErrBuilder.ToString().Trim() : string.Empty;
        return new ExecutionResult(exitCode, stdOutResult, stdErrResult);
    }
}