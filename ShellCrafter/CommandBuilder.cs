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
    private IProgress<StatusUpdate>? _progressHandler = null;

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

    public CommandBuilder WithProgress(IProgress<StatusUpdate> progress)
    {
        _progressHandler = progress ?? throw new ArgumentNullException(nameof(progress));
        return this;
    }

    // --- Execution Orchestrator ---
    public async Task<ExecutionResult> ExecuteAsync(
        CancellationToken cancellationToken = default,
        bool killOnCancel = false)
    {
        var processStartInfo = ConfigureProcessStartInfo();

        using var process = new Process { StartInfo = processStartInfo };
        if (process == null) { throw new InvalidOperationException($"Failed to create process for {_executable}."); }

        var stdOutputBuilder = new StringBuilder();
        var stdErrorBuilder = new StringBuilder();
        var outputCloseEvent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); // Use option for safety
        var errorCloseEvent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        AttachEventHandlers(process, stdOutputBuilder, stdErrorBuilder, outputCloseEvent, errorCloseEvent);

        StartProcess(process); // Separated Start from Attach/BeginRead

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await WriteStandardInputAsync(process);

        // Handles wait, cancellation, kill, and waits for streams
        await HandleProcessExitAsync(process, killOnCancel, outputCloseEvent.Task, errorCloseEvent.Task, cancellationToken);

        // If HandleProcessExitAsync didn't throw (i.e., no cancellation), create result
        var finalResult = CreateFinalResult(process, stdOutputBuilder, stdErrorBuilder);

        // Report exit and return
        _progressHandler?.Report(new ProcessExited(finalResult));
        return finalResult;
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
        if (!string.IsNullOrEmpty(_standardInput))
        {
            // StandardInput might be null if process creation failed subtly or RedirectStandardInput=false
            if (process.StandardInput == null)
            {
                throw new InvalidOperationException("Standard input stream is null. Cannot write input.");
            }
            using (StreamWriter standardInputWriter = process.StandardInput)
            {
                await standardInputWriter.WriteAsync(_standardInput);
            }
        }
    }

    private async Task HandleProcessExitAsync(Process process, bool killOnCancel, Task outputCompletionTask, Task errorCompletionTask, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(outputCompletionTask, errorCompletionTask);
            //Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Process {process.Id}: Wait completed NORMALLY."); // Debug
        }
        catch (OperationCanceledException)
        {
            //Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Process {process.Id}: Caught OperationCanceledException."); // Debug
            if (killOnCancel) // Only attempt kill if flag is true
            {
                // Check HasExited defensively before Kill
                try
                {
                    if (!process.HasExited)
                    {
                        //Console.WriteLine($"Attempting to kill process {process.Id}..."); // Debug
                        process.Kill();
                        // Consider process.Kill(true) on .NET 5+
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is NotSupportedException || ex is System.ComponentModel.Win32Exception)
                {
                    //Console.WriteLine($"Failed or unnecessary to kill process {process.Id}: {ex.Message}"); // Debug
                    // Swallow exception - best effort kill
                }
            }
            throw; // Always re-throw cancellation exception
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