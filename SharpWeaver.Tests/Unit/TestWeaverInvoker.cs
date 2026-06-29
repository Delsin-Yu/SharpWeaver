using System.Diagnostics;

namespace SharpWeaver.Tests;

/// <summary>Launches the SharpWeaver CLI from tests with live subprocess progress.</summary>
internal static class TestWeaverInvoker
{
    /// <summary>Runs SharpWeaver against an assembly.</summary>
    /// <param name="assemblyPath">Target assembly path.</param>
    /// <param name="references">Reference assembly paths passed to SharpWeaver.</param>
    /// <param name="error">Captured stderr lines on completion.</param>
    /// <returns>SharpWeaver process exit code.</returns>
    public static int RunWeave(string assemblyPath, IReadOnlyList<string> references, out IReadOnlyList<string> error)
    {
        return Run(
            assemblyPath,
            references,
            dryRun: false,
            out _,
            out error);
    }

    /// <summary>Runs SharpWeaver in dry-run mode against an assembly.</summary>
    /// <param name="assemblyPath">Target assembly path.</param>
    /// <param name="references">Reference assembly paths passed to SharpWeaver.</param>
    /// <param name="output">Captured stdout text on completion.</param>
    /// <param name="error">Captured stderr lines on completion.</param>
    /// <returns>SharpWeaver process exit code.</returns>
    public static int RunDryRun(
        string assemblyPath,
        IReadOnlyList<string> references,
        out string output,
        out IReadOnlyList<string> error)
    {
        return Run(
            assemblyPath,
            references,
            dryRun: true,
            out output,
            out error);
    }

    private static int Run(
        string assemblyPath,
        IReadOnlyList<string> references,
        bool dryRun,
        out string output,
        out IReadOnlyList<string> error)
    {
        var toolPath = FixtureBuildHelper.WeaverToolPath;
        var refsArg = string.Join(";", references);
        var dryRunArg = dryRun ? "--dry-run " : string.Empty;
        var mode = dryRun ? "dry-run" : "weave";
        var progressLabel = $"SharpWeaver {mode} ({Path.GetFileName(assemblyPath)})";

        var (exitCode, stdout, stderr) = TestProcessRunner.Run(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{toolPath}\" {dryRunArg}--assembly \"{assemblyPath}\" --references \"{refsArg}\"",
        }, progressLabel);

        output = stdout;
        error = stderr
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        return exitCode;
    }
}
