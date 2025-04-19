// ShellCrafter/CommandExecutor.cs
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
    // --- State ---
    private readonly CommandConfiguration _config; // Holds all settings

    // --- Constructor ---
    internal CommandBuilder(string executable)
    {
        // Initialize configuration with the required executable
        _config = new CommandConfiguration(executable ?? throw new ArgumentNullException(nameof(executable)));
    }

    // --- Fluent Configuration Methods (Modify the config object) ---
    public CommandBuilder WithArguments(params string[] args)
    {
        _config.Arguments.AddRange(args);
        return this;
    }

    public CommandBuilder InWorkingDirectory(string path)
    {
        _config.WorkingDirectory = path;
        return this;
    }

    public CommandBuilder WithEnvironmentVariable(string key, string? value)
    {
        if (string.IsNullOrEmpty(key)) { throw new ArgumentException("Environment variable key cannot be null or empty.", nameof(key)); }
        _config.EnvironmentVariables[key] = value;
        return this;
    }

    public CommandBuilder WithEnvironmentVariables(IDictionary<string, string?> variables)
    {
        if (variables == null) { throw new ArgumentNullException(nameof(variables)); }
        foreach (var kvp in variables)
        {
            if (string.IsNullOrEmpty(kvp.Key)) { throw new ArgumentException("Environment variable keys in the dictionary cannot be null or empty.", nameof(variables)); }
            _config.EnvironmentVariables[kvp.Key] = kvp.Value;
        }
        return this;
    }

    public CommandBuilder WithStandardInput(string input)
    {
        _config.StandardInputString = input ?? throw new ArgumentNullException(nameof(input));
        _config.StandardInputStream = null; // Ensure only one is set
        return this;
    }

    public CommandBuilder WithStandardInput(Stream inputStream)
    {
        _config.StandardInputStream = inputStream ?? throw new ArgumentNullException(nameof(inputStream));
        _config.StandardInputString = null; // Ensure only one is set
        return this;
    }

    public CommandBuilder PipeStandardOutputTo(Stream target, bool captureInternal = false)
    {
        _config.StdOutPipeTarget = target ?? throw new ArgumentNullException(nameof(target));
        if (!target.CanWrite) throw new ArgumentException("Target stream must be writable.", nameof(target));
        _config.CaptureStdOut = captureInternal;
        return this;
    }

    public CommandBuilder PipeStandardErrorTo(Stream target, bool captureInternal = false)
    {
        _config.StdErrPipeTarget = target ?? throw new ArgumentNullException(nameof(target));
        if (!target.CanWrite) throw new ArgumentException("Target stream must be writable.", nameof(target));
        _config.CaptureStdErr = captureInternal;
        return this;
    }

    public CommandBuilder WithProgress(IProgress<StatusUpdate> progress)
    {
        _config.ProgressHandler = progress ?? throw new ArgumentNullException(nameof(progress));
        return this;
    }

    public CommandBuilder WithTimeout(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero && duration != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Timeout duration must be positive or Timeout.InfiniteTimeSpan.");
        }
        _config.Timeout = duration;
        return this;
    }

    // --- Execution Trigger ---
    // This method now delegates the actual work to the CommandExecutor
    public Task<ExecutionResult> ExecuteAsync(
        CancellationToken cancellationToken = default,
        KillMode killMode = KillMode.NoKill)
    {
        // Pass the fully configured object and execution parameters to the static executor
        return CommandExecutor.ExecuteAsync(_config, cancellationToken, killMode);
    }

}
