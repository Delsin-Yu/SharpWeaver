using System.Runtime.CompilerServices;

namespace SharpWeaver.TestFixtures.Fake;

/// <summary>Targets that return managed references and must not be sync-woven.</summary>
public static class ByRefReturnTarget<T> where T : struct
{
    private static T Value;

    /// <summary>Stores the value returned by <see cref="GetValueRefOrNullRefReadOnly"/>.</summary>
    /// <param name="value">Value to expose by reference.</param>
    public static void SetValue(T value) => Value = value;

    /// <summary>Returns a readonly reference to the stored value, or a null ref when disabled.</summary>
    /// <param name="enabled">Whether a stored value should be returned.</param>
    /// <param name="hasValue">Whether the returned reference is valid.</param>
    /// <returns>A readonly reference to the stored value, or <see cref="Unsafe.NullRef{T}"/>.</returns>
    public static ref readonly T GetValueRefOrNullRefReadOnly(bool enabled, out bool hasValue)
    {
        if (!enabled)
        {
            hasValue = false;
            return ref Unsafe.NullRef<T>();
        }

        hasValue = true;
        return ref Value;
    }
}
