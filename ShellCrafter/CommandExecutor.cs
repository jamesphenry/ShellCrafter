// ShellCrafter/CommandExecutor.cs
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("ShellCrafter.Tests")]

namespace ShellCrafter;

/// <summary>
/// Handles the actual execution of the configured command.
/// </summary>
internal static class CommandExecutor
{
    // --- Main Execution Method ---
    internal static async Task<ExecutionResult> ExecuteAsync(
        CommandConfiguration config, // Pass config object
        CancellationToken cancellationToken,
        KillMode killMode)
    {
        var processStartInfo = ConfigureProcessStartInfo(config); // Pass config

        using var process = new Process { StartInfo = processStartInfo };
        if (process == null) { throw new InvalidOperationException($"Failed to create process for {config.Executable}."); }

        // --- Timeout and Cancellation Handling Setup ---
        CancellationTokenSource? internalTimeoutCts = null;
        CancellationTokenSource? linkedCts = null;
        CancellationToken effectiveToken = cancellationToken;
        if (config.Timeout.HasValue && config.Timeout.Value != Timeout.InfiniteTimeSpan)
        {
            internalTimeoutCts = new CancellationTokenSource();
            try { internalTimeoutCts.CancelAfter(config.Timeout.Value); }
            catch (ArgumentOutOfRangeException ex)
            {
                internalTimeoutCts.Dispose();
                throw new ArgumentOutOfRangeException(nameof(config.Timeout), $"Invalid timeout value provided: {config.Timeout.Value}", ex.Message);
            }
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, internalTimeoutCts.Token);
            effectiveToken = linkedCts.Token;
        }
        // --- End Timeout Setup ---

        // --- Output/Error Handling Setup ---
        var stdOutputBuilder = config.CaptureStdOut ? new StringBuilder() : null;
        var stdErrorBuilder = config.CaptureStdErr ? new StringBuilder() : null;
        Task outputCompletionTask = Task.CompletedTask;
        Task errorCompletionTask = Task.CompletedTask;
        TaskCompletionSource<bool>? outputCloseEvent = null;
        TaskCompletionSource<bool>? errorCloseEvent = null;
        // --- End Output/Error Handling Setup ---

        ExecutionResult finalResult;

        try
        {
            StartProcess(process, config); // Start process, report ProcessStarted

            // --- Handle Standard Output ---
            if (config.StdOutPipeTarget != null)
            {
                outputCompletionTask = process.StandardOutput.BaseStream.CopyToAsync(config.StdOutPipeTarget, effectiveToken);
            }
            else if (config.CaptureStdOut && stdOutputBuilder != null)
            {
                outputCloseEvent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                SetupEventBasedReading(process, stdOutputBuilder, config.ProgressHandler, outputCloseEvent, true); // isStdOut = true
                outputCompletionTask = outputCloseEvent.Task;
            }

            // --- Handle Standard Error ---
            if (config.StdErrPipeTarget != null)
            {
                errorCompletionTask = process.StandardError.BaseStream.CopyToAsync(config.StdErrPipeTarget, effectiveToken);
            }
            else if (config.CaptureStdErr && stdErrorBuilder != null)
            {
                errorCloseEvent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                SetupEventBasedReading(process, stdErrorBuilder, config.ProgressHandler, errorCloseEvent, false); // isStdOut = false
                errorCompletionTask = errorCloseEvent.Task;
            }

            await WriteStandardInputAsync(process, config); // Write input after starting reads/pipes

            // Handles wait, cancellation, kill, and waits for stream tasks
            await HandleProcessExitAsync(process, config, killMode, outputCompletionTask, errorCompletionTask, effectiveToken, internalTimeoutCts);

            // If HandleProcessExitAsync didn't throw, create result
            finalResult = CreateFinalResult(process, config, stdOutputBuilder, stdErrorBuilder);

            // Report exit
            config.ProgressHandler?.Report(new ProcessExited(finalResult));
            return finalResult;
        }
        finally
        {
            internalTimeoutCts?.Dispose();
            linkedCts?.Dispose();
        }
    }

    // --- Private Static Helper Methods ---

    private static ProcessStartInfo ConfigureProcessStartInfo(CommandConfiguration config)
    {
        var psi = new ProcessStartInfo
        {
            FileName = config.Executable,
            Arguments = string.Join(" ", config.Arguments.Select(EscapeArgument)),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = config.WorkingDirectory
        };
        if (config.EnvironmentVariables.Count > 0)
        {
            foreach (var kvp in config.EnvironmentVariables) { psi.EnvironmentVariables[kvp.Key] = kvp.Value; }
        }
        return psi;
    }

    private static void SetupEventBasedReading(Process process, StringBuilder builder, IProgress<StatusUpdate>? progressHandler, TaskCompletionSource<bool> completionSource, bool isStdOut)
    {
        if (isStdOut)
        {
            process.OutputDataReceived += (sender, e) =>
              e.HandleDataReceived(builder, progressHandler, completionSource, data => new StdOutDataReceived(data));
            process.BeginOutputReadLine();
        }
        else
        {
            process.ErrorDataReceived += (sender, e) =>
              e.HandleDataReceived(builder, progressHandler, completionSource, data => new StdErrDataReceived(data));
            process.BeginErrorReadLine();
        }
    }

    private static void StartProcess(Process process, CommandConfiguration config)
    {
        bool isStarted;
        try { isStarted = process.Start(); }
        catch (Exception ex) { throw new InvalidOperationException($"Failed to start process '{config.Executable}'. Verify path/permissions.", ex); }
        if (!isStarted) { throw new InvalidOperationException($"Process failed to start for executable: {config.Executable}"); }
        config.ProgressHandler?.Report(new ProcessStarted(process.Id));
    }

    private static async Task WriteStandardInputAsync(Process process, CommandConfiguration config)
    {
        if (config.StandardInputStream != null)
        {
            if (!config.StandardInputStream.CanRead) { throw new InvalidOperationException("Provided standard input stream is not readable."); }
            using (var standardInputWriter = process.StandardInput) { await config.StandardInputStream.CopyToAsync(standardInputWriter.BaseStream); }
        }
        else if (!string.IsNullOrEmpty(config.StandardInputString))
        {
            if (process.StandardInput == null) { throw new InvalidOperationException("Standard input stream is null. Cannot write input."); }
            using (StreamWriter standardInputWriter = process.StandardInput) { await standardInputWriter.WriteAsync(config.StandardInputString); }
        }
    }

    private static async Task HandleProcessExitAsync(Process process, CommandConfiguration config, KillMode killMode, Task outputCompletionTask, Task errorCompletionTask, CancellationToken effectiveToken, CancellationTokenSource? internalTimeoutCts)
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
                if (killMode != KillMode.NoKill) { AttemptKillProcess(process, killMode); }
                throw new TimeoutException($"The operation timed out after {config.Timeout!.Value}.", ex);
            }
            else // External Cancellation
            {
                if (killMode != KillMode.NoKill) { AttemptKillProcess(process, killMode); }
                throw;
            }
        }
    }

    private static void AttemptKillProcess(Process process, KillMode killMode) // Completed method
    {
        if (killMode == KillMode.NoKill) return;
        try
        {
            if (!process.HasExited)
            {
                if (killMode == KillMode.ProcessTree)
                {
#if NET5_0_OR_GREATER
                    // Console.WriteLine($"Attempting to kill process tree starting with {process.Id}..."); 
                    process.Kill(true);
#else
                    // Console.WriteLine($"Attempting to kill root process {process.Id} (Tree kill not supported)..."); 
                    process.Kill(); 
#endif
                }
                else // killMode == KillMode.RootProcess
                {
                    // Console.WriteLine($"Attempting to kill root process {process.Id}..."); 
                    process.Kill();
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is NotSupportedException || ex is System.ComponentModel.Win32Exception)
        {
            // Console.WriteLine($"Failed or unnecessary to kill process {process.Id}: {ex.Message}"); 
            // Swallow exception - best effort kill
        }
    }

    private static ExecutionResult CreateFinalResult(Process process, CommandConfiguration config, StringBuilder? stdOutBuilder, StringBuilder? stdErrBuilder) // Takes config
    {
        int exitCode = process.ExitCode;
        // Use config flags
        string stdOutResult = config.CaptureStdOut && stdOutBuilder != null ? stdOutBuilder.ToString().Trim() : string.Empty;
        string stdErrResult = config.CaptureStdErr && stdErrBuilder != null ? stdErrBuilder.ToString().Trim() : string.Empty;
        return new ExecutionResult(exitCode, stdOutResult, stdErrResult);
    }

    // Escapes an argument for safe command-line parsing (basic implementation)
    internal static string EscapeArgument(string arg) // Make internal if it was private
    {
        if (!NeedsEscaping(arg))
        {
            return arg;
        }

        var sb = new StringBuilder("\"");
        foreach (char c in arg)
        {
            if (c == '\\') sb.Append("\\\\");
            else if (c == '"') sb.Append("\\\"");
            else sb.Append(c);
        }
        sb.Append("\"");
        return sb.ToString();
    }

    // We will also need a NeedsEscaping helper later
    private static bool NeedsEscaping(string arg)
    {
        // Quote empty strings. Quote strings containing whitespace or quotes.
        // Make sure char.IsWhiteSpace(c) is used correctly within the Any() lambda.
        return string.IsNullOrEmpty(arg) || arg.Any(c => char.IsWhiteSpace(c) || c == '"');
    }
}
