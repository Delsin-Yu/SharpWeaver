using System.Diagnostics;

namespace SharpWeaver.Tests;

/// <summary>Thread-safe, timestamped progress lines for long-running SharpWeaver test runs.</summary>
internal static class TestProgressReporter
{
    private static readonly Lock ConsoleLock = new();

    /// <summary>Prints a banner line at assembly start or finish.</summary>
    /// <param name="message">Banner text.</param>
    public static void WriteBanner(string message)
    {
        WriteLine($"=== {message} ===");
    }

    /// <summary>Prints when an individual test invocation starts.</summary>
    /// <param name="displayName">Fully qualified test display name.</param>
    /// <param name="index">1-based invocation index for this run.</param>
    public static void WriteTestStarting(string displayName, int index)
    {
        WriteLine($">>> [{index}] START  {displayName}");
    }

    /// <summary>Prints when an individual test method body completes.</summary>
    /// <param name="displayName">Fully qualified test display name.</param>
    /// <param name="elapsed">Wall time spent inside the test method.</param>
    public static void WriteTestFinished(string displayName, TimeSpan elapsed)
    {
        WriteLine($"<<< DONE {elapsed.TotalSeconds,8:F2}s  {displayName}");
    }

    /// <summary>Prints when an individual test invocation passes.</summary>
    /// <param name="displayName">Fully qualified test display name.</param>
    /// <param name="executionTime">Test execution duration reported by xUnit.</param>
    public static void WriteTestPassed(string displayName, decimal executionTime)
    {
        WriteLine($"<<< PASS {FormatDuration(executionTime)}  {displayName}");
    }

    /// <summary>Prints when an individual test invocation fails.</summary>
    /// <param name="displayName">Fully qualified test display name.</param>
    /// <param name="executionTime">Test execution duration reported by xUnit.</param>
    /// <param name="summary">First failure message, when available.</param>
    public static void WriteTestFailed(string displayName, decimal executionTime, string? summary)
    {
        var detail = string.IsNullOrWhiteSpace(summary) ? string.Empty : $" | {summary}";
        WriteLine($"<<< FAIL {FormatDuration(executionTime)}  {displayName}{detail}");
    }

    /// <summary>Prints when an individual test invocation is skipped.</summary>
    /// <param name="displayName">Fully qualified test display name.</param>
    /// <param name="reason">Skip reason from xUnit.</param>
    public static void WriteTestSkipped(string displayName, string reason)
    {
        WriteLine($"<<< SKIP          {displayName} | {reason}");
    }

    /// <summary>Prints when a child process used by a test starts.</summary>
    /// <param name="label">Short human-readable operation label.</param>
    public static void WriteSubprocessStarting(string label)
    {
        WriteLine($"... subprocess START  {label}");
    }

    /// <summary>Prints when a child process used by a test completes.</summary>
    /// <param name="label">Short human-readable operation label.</param>
    /// <param name="exitCode">Child process exit code.</param>
    /// <param name="elapsed">Child process wall time.</param>
    public static void WriteSubprocessFinished(string label, int exitCode, TimeSpan elapsed)
    {
        WriteLine($"... subprocess DONE   {label} exit={exitCode} elapsed={elapsed.TotalSeconds:F1}s");
    }

    private static string FormatDuration(decimal executionTimeSeconds) =>
        $"{executionTimeSeconds,8:F2}s";

    private static void WriteLine(string message)
    {
        lock (ConsoleLock)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Console.Error.WriteLine(line);
            Console.Error.Flush();
        }
    }
}
