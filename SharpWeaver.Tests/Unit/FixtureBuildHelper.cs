using System.Diagnostics;

namespace SharpWeaver.Tests;

/// <summary>Shared helper methods for building IL post-processor test fixture assemblies.</summary>
internal static class FixtureBuildHelper
{
    /// <summary>SharpWeaver repository root directory.</summary>
    internal static string ProjectRoot { get; } = ResolveProjectRoot();

    /// <summary>Unified test tree directory under the repository root.</summary>
    internal static string TestsDirectory { get; } = Path.Combine(ProjectRoot, "SharpWeaver.Tests");

    private static string ResolveProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, "SharpWeaver.Tests", "Fixtures"))
                && File.Exists(Path.Combine(dir, "SharpWeaver", "SharpWeaver.csproj")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "无法定位 SharpWeaver 测试项目根目录；请从 SharpWeaver.Tests/Unit 项目输出运行测试。");
    }

    private static readonly string DefaultFixturesOutputDir = Path.Combine(
        TestsDirectory, "Fixtures", "bin", "Debug", "net10.0");

    /// <summary>Primary test fixture assembly output directory (prefers build artifacts co-located with the test assembly).</summary>
    internal static string FixturesOutputDir { get; } = ResolveFixturesOutputDir();

    private static string ResolveFixturesOutputDir()
    {
        var coLocatedDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(coLocatedDir, "SharpWeaver.TestFixtures.dll")))
        {
            return coLocatedDir;
        }

        return DefaultFixturesOutputDir;
    }

    private static readonly string BadSignatureOutputDir = Path.Combine(
        TestsDirectory, "BadSignature", "bin", "Debug", "net10.0");

    private static readonly string InstancePatchOutputDir = Path.Combine(
        TestsDirectory, "InstancePatch", "bin", "Debug", "net10.0");

    private static readonly string DuplicatePrefixOutputDir = Path.Combine(
        TestsDirectory, "DuplicatePrefix", "bin", "Debug", "net10.0");

    /// <summary>Primary test fixture assembly path.</summary>
    public static string FixtureAssemblyPath { get; } = Path.Combine(
        FixturesOutputDir, "SharpWeaver.TestFixtures.dll");

    /// <summary>IL post-processor tool path (prefers build artifacts co-located with the test assembly).</summary>
    internal static string WeaverToolPath { get; } = ResolveWeaverToolPath();

    private static string ResolveWeaverToolPath()
    {
        var coLocated = Path.Combine(AppContext.BaseDirectory, "SharpWeaver.dll");
        if (File.Exists(coLocated))
        {
            return coLocated;
        }

        return Path.Combine(ProjectRoot, "SharpWeaver", "bin", "Debug", "net10.0", "SharpWeaver.dll");
    }

    private static readonly string BadSignatureAssemblyPath = Path.Combine(
        BadSignatureOutputDir, "SharpWeaver.TestFixtures.BadSignature.dll");

    private static readonly string InstancePatchAssemblyPath = Path.Combine(
        InstancePatchOutputDir, "SharpWeaver.TestFixtures.InstancePatch.dll");

    private static readonly string DuplicatePrefixAssemblyPath = Path.Combine(
        DuplicatePrefixOutputDir, "SharpWeaver.TestFixtures.DuplicatePrefix.dll");

    /// <summary>Builds all test fixture assemblies when needed.</summary>
    public static void EnsureAllFixturesBuilt()
    {
        if (File.Exists(FixtureAssemblyPath)
            && File.Exists(BadSignatureAssemblyPath)
            && File.Exists(InstancePatchAssemblyPath)
            && File.Exists(DuplicatePrefixAssemblyPath))
        {
            return;
        }

        var testsProjectPath = Path.Combine(TestsDirectory, "Unit", "SharpWeaver.Tests.csproj");
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{testsProjectPath}\" -c Debug",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet build for test fixtures.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException(
                $"Fixture build failed with exit code {process.ExitCode}.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }
    }
}
