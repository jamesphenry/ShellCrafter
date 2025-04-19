// ShellCrafter/ProcessDataReceivedHandlerExtensions.cs
using System;
using System.Diagnostics; // For DataReceivedEventArgs
using System.Text;      // For StringBuilder
using System.Threading.Tasks; // For TaskCompletionSource

namespace ShellCrafter; // Use your project's namespace

internal static class ProcessDataReceivedHandlerExtensions
{
    /// <summary>
    /// Handles received data from a process stream (stdout/stderr).
    /// Checks for stream close, trims data, appends to builder, and reports progress.
    /// </summary>
    /// <param name="e">The event arguments containing the data.</param>
    /// <param name="builder">The StringBuilder to append the trimmed data line to.</param>
    /// <param name="progressHandler">The progress handler to report updates to.</param>
    /// <param name="completionSource">The TaskCompletionSource to signal when the stream is closed (e.Data == null).</param>
    /// <param name="statusUpdateFactory">A function that creates the appropriate StatusUpdate record (e.g., StdOutDataReceived or StdErrDataReceived) from the trimmed data string.</param>
    public static void HandleDataReceived(
        this DataReceivedEventArgs e, // Make it an extension method on the event args
        StringBuilder builder,
        IProgress<StatusUpdate>? progressHandler,
        TaskCompletionSource<bool> completionSource,
        Func<string, StatusUpdate> statusUpdateFactory) // Factory function for status type
    {
        // Check for stream end signal first
        if (e.Data == null)
        {
            completionSource.TrySetResult(true);
            return;
        }

        // Process the data line
        var trimmedData = e.Data.Trim();
        builder.AppendLine(trimmedData);
        progressHandler?.Report(statusUpdateFactory(trimmedData)); // Use factory to create correct update type
    }
}