namespace SharpWeaver.TestFixtures.ExternalBase;

/// <summary>External-framework tick host used to exercise override weaving against a foreign base type.</summary>
public class TickHost
{
    /// <summary>Virtual tick callback invoked by host frameworks.</summary>
    /// <param name="delta">Elapsed time since the previous tick.</param>
    public virtual void Tick(double delta)
    {
    }
}
