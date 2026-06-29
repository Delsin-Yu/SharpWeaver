using System.Diagnostics;

namespace SharpWeaver.Tests;

/// <summary>Runs child processes without deadlocking on redirected stdout/stderr pipes.</summary>
internal static class TestProcessRunner
{
    /// <summary>Starts a process, drains stdout/stderr concurrently, and waits for exit.</summary>
    /// <param name="startInfo">Process start configuration with redirected streams.</param>
    /// <param name="progressLabel">Optional label printed when the subprocess starts and finishes.</param>
    /// <returns>Exit code and captured stdout/stderr text.</returns>
    public static (int ExitCode, string StdOut, string StdErr) Run(
        ProcessStartInfo startInfo,
        string? progressLabel = null)
    {
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        if (progressLabel is not null)
        {
            TestProgressReporter.WriteSubprocessStarting(progressLabel);
        }

        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start child process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        stopwatch.Stop();

        if (progressLabel is not null)
        {
            TestProgressReporter.WriteSubprocessFinished(progressLabel, process.ExitCode, stopwatch.Elapsed);
        }

        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }
}
