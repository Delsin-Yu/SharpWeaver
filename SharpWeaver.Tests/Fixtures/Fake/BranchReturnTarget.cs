namespace SharpWeaver.TestFixtures.Fake;

/// <summary>
/// Test target with non-void return and <c>br</c> instructions converging on a single <c>ret</c> (similar to user-defined conversion).
/// </summary>
public static class BranchReturnTarget
{
    /// <summary>
    /// Increments a nullable input by one; returns <see langword="null"/> when null.
    /// </summary>
    /// <param name="input">Input value.</param>
    /// <returns>Incremented nullable integer, or <see langword="null"/>.</returns>
    public static int? Convert(int? input)
    {
        if (input.HasValue)
        {
            return input.Value + 1;
        }

        return null;
    }
}
