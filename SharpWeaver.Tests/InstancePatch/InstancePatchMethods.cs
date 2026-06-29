using SharpWeaver;

namespace SharpWeaver.TestFixtures.InstancePatch;

/// <summary>Type with instance-level prefix patches and instance-level weave methods (for testing, should be rejected).</summary>
public class InstancePatchMethods
{
    /// <summary>Non-static ILWeaving weave method — should be rejected by WeaveScanner.</summary>
    [Weave("SharpWeaver.TestFixtures.ExternalBase.TickHost.Tick(double)", priority: 0)]
    public void InstanceILWeaving()
    {
        WeaveTemplate.OriginalBody();
    }
}
