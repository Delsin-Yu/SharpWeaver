using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Xunit.Sdk;

namespace SharpWeaver.Tests;

/// <summary>Prints live start/finish lines for each test method invocation to stderr.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TestProgressAttribute : BeforeAfterTestAttribute
{
    private static int _startedTests;
    private static readonly ConcurrentDictionary<string, Stopwatch> ActiveStopwatches = new();

    /// <inheritdoc />
    public override void Before(MethodInfo methodUnderTest)
    {
        var displayName = FormatDisplayName(methodUnderTest);
        ActiveStopwatches[displayName] = Stopwatch.StartNew();
        TestProgressReporter.WriteTestStarting(displayName, Interlocked.Increment(ref _startedTests));
    }

    /// <inheritdoc />
    public override void After(MethodInfo methodUnderTest)
    {
        var displayName = FormatDisplayName(methodUnderTest);
        if (ActiveStopwatches.TryRemove(displayName, out var stopwatch))
        {
            TestProgressReporter.WriteTestFinished(displayName, stopwatch.Elapsed);
        }
    }

    private static string FormatDisplayName(MethodInfo methodUnderTest) =>
        $"{methodUnderTest.DeclaringType!.FullName}.{methodUnderTest.Name}";
}
