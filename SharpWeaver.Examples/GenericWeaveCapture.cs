using SharpWeaver;

namespace SharpWeaver.Examples.GenericWeaveCapture;

/// <summary>User code — open generic method.</summary>
public sealed class GenericBox
{
    /// <summary>Echoes a typed value.</summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="value">Input value.</param>
    /// <returns>Same value.</returns>
    public T Echo<T>(T value)
    {
        return value;
    }
}

/// <summary>Weave patch — records generic parameter names at weave time.</summary>
public static class GenericBoxWeavePatch
{
    [Weave("SharpWeaver.Examples.GenericWeaveCapture.GenericBox.Echo(**)", priority: 0, genericWeave: true)]
    public static void EchoWeave(
        object? instance,
        [WeaveTypeParams] Type[] genericTypeParams,
        [WeaveMethodName] string methodName)
    {
        var typeNames = new string[genericTypeParams.Length];
        for (var i = 0; i < genericTypeParams.Length; i++)
        {
            typeNames[i] = genericTypeParams[i].Name;
        }

        Console.WriteLine($"[generic] {methodName}<{string.Join(", ", typeNames)}>");
        WeaveTemplate.OriginalBody();
    }
}
