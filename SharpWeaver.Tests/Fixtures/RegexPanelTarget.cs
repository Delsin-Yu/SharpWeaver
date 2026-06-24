namespace SharpWeaver.TestFixtures.Fake;

/// <summary>Type used for regex ILWeaving matching tests.</summary>
public class RegexPanelTarget
{
    /// <summary>Method that should be matched by <c>^.*\._OnPanelOpen\(\)$</c> regex.</summary>
    public void _OnPanelOpen()
    {
        BehavioralState.RegexPanelOpenRuns++;
    }

    /// <summary>Method that should not be matched by the panel regex.</summary>
    public void OtherMethod()
    {
        BehavioralState.RegexOtherMethodRuns++;
    }
}
