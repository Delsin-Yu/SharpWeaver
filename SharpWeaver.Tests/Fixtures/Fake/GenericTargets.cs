namespace SharpWeaver.TestFixtures.Fake;

/// <summary>Generic weave test targets.</summary>
public class GenericMethodTarget
{
    /// <summary>Open generic method target.</summary>
    public T Echo<T>(T value)
    {
        BehavioralState.GenericMethodBodyRuns++;
        return value;
    }

    /// <summary>Non-generic same-namespace target, used to verify it is not matched by generic weave templates.</summary>
    public string NonGenericEcho(string value)
    {
        BehavioralState.GenericNonGenericBodyRuns++;
        return value;
    }
}

/// <summary>Generic declaring type weave test target.</summary>
public class GenericContainer<T>
{
    /// <summary>Ordinary method target whose declaring type has open generic parameters.</summary>
    public T Run(T value)
    {
        BehavioralState.GenericDeclaringTypeBodyRuns++;
        return value;
    }
}
