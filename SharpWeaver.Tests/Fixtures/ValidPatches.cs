using System;
using SharpWeaver;
using SharpWeaver.TestFixtures.Fake;
using SharpWeaver.TestFixtures.Godot;

namespace SharpWeaver.TestFixtures;

/// <summary>ILWeaving weave method definitions for testing.</summary>
public static class ValidPatches
{
    /// <summary>
    /// ILWeaving weave for <c>FakeBase.DoWork(int)</c>: executes postfix tracking after the original method body.
    /// </summary>
    /// <param name="instance">Target instance (by value).</param>
    /// <param name="value">Input value (ref form to satisfy weave signature contract).</param>
    [Weave("SharpWeaver.TestFixtures.Fake.FakeBase.DoWork(int)", priority: 0)]
    public static void FakeWorkWeave(FakeBase instance, ref int value)
    {
        WeaveTemplate.OriginalBody();
        BehavioralState.DoWorkPostfixRuns++;
        BehavioralState.DoWorkTrace.Add("postfix");
    }

    /// <summary>
    /// ILWeaving weave for <c>Godot.Node._Process(double)</c>: wraps the original method body with <c>try/catch</c>,
    /// logs caught exceptions and rethrows.
    /// </summary>
    /// <param name="instance">Target instance (by value).</param>
    /// <param name="delta">Frame delta (ref form to satisfy weave signature contract).</param>
    [Weave("Godot.Node._Process(double)", priority: 0)]
    public static void ProcessWeave(GodotProcessNode instance, ref double delta)
    {
        _ = instance;
        _ = delta;
        try
        {
            WeaveTemplate.OriginalBody();
        }
        catch (Exception ex)
        {
            BehavioralState.ExceptionWrapHandlerRuns++;
            BehavioralState.LastCaughtExceptionMessage = ex.Message;
            throw;
        }
    }

    /// <summary>
    /// ILWeaving weave for <c>IntReturnBase.GetValue()</c>: if <see cref="BehavioralState.IntReturnPrefixValue"/>
    /// has a value, skips the original method body and returns that value; otherwise executes the original body.
    /// </summary>
    /// <param name="instance">Target instance (by value).</param>
    /// <param name="returnValue">Return value slot: writing to it causes an early-return to skip the original body.</param>
    [Weave("SharpWeaver.TestFixtures.Fake.IntReturnBase.GetValue()", priority: 0)]
    public static void IntReturnWeave(IntReturnBase instance, ref int returnValue)
    {
        _ = instance;
        if (BehavioralState.IntReturnPrefixValue.HasValue)
        {
            returnValue = BehavioralState.IntReturnPrefixValue.Value;
            return;
        }

        WeaveTemplate.OriginalBody();
    }

    /// <summary>
    /// ILWeaving weave for <c>ThrowingBase.MayThrow(int)</c>: wraps the original method body with <c>try/catch</c>,
    /// updates <see cref="BehavioralState"/> on exception and rethrows.
    /// </summary>
    /// <param name="instance">Target instance (by value).</param>
    /// <param name="value">Input value (ref form to satisfy weave signature contract).</param>
    [Weave("SharpWeaver.TestFixtures.Fake.ThrowingBase.MayThrow(int)", priority: 0)]
    public static void MayThrowWeave(ThrowingBase instance, ref int value)
    {
        _ = instance;
        _ = value;
        try
        {
            WeaveTemplate.OriginalBody();
        }
        catch (Exception ex)
        {
            BehavioralState.ExceptionWrapHandlerRuns++;
            BehavioralState.LastCaughtExceptionMessage = ex.Message;
            throw;
        }
    }

    /// <summary>
    /// ILWeaving spike: weave for <c>WeaveBase.WeaveWork(int)</c>, verifying prefix (none) → original body → postfix order.
    /// </summary>
    /// <param name="instance">Target instance (by value).</param>
    /// <param name="value">Input value.</param>
    [Weave("SharpWeaver.TestFixtures.Fake.WeaveBase.WeaveWork(int)", priority: 0)]
    public static void WeaveWorkWeave(WeaveBase instance, int value)
    {
        _ = instance;
        _ = value;
        WeaveTemplate.OriginalBody();
        BehavioralState.WeaveWorkPostfixRuns++;
        BehavioralState.WeaveWorkTrace.Add("weave_postfix");
    }

    /// <summary>
    /// Wildcard weave <c>**.*._OnPanelOpen()</c>: records captured method name in prefix, then executes original body.
    /// </summary>
    /// <param name="instance">Target instance.</param>
    /// <param name="typeName">Injected target type fully qualified name.</param>
    /// <param name="methodName">Injected target method name.</param>
    [Weave("**.*._OnPanelOpen()", priority: 5)]
    public static void RegexPanelWeave(
        object? instance,
        [WeaveTypeName] string typeName,
        [WeaveMethodName] string methodName)
    {
        _ = instance;
        BehavioralState.RegexCapturedTypeName = typeName;
        BehavioralState.RegexCapturedMethodName = methodName;
        WeaveTemplate.OriginalBody();
    }

    /// <summary>
    /// Exact weave <c>WeaveCalleeTarget.Run()</c>: calls infrastructure method, then executes original body.
    /// </summary>
    /// <param name="instance">Target instance.</param>
    [Weave("SharpWeaver.TestFixtures.Fake.WeaveCalleeTarget.Run(**)", priority: 5)]
    public static void WeaveCalleeTargetWeave(object? instance)
    {
        _ = instance;
        WeaveCalleeInfra.Touch();
        WeaveTemplate.OriginalBody();
    }

    /// <summary>
    /// Two exact signatures on the same weave method: matching <c>MultiPatternTarget.Alpha/Beta</c> respectively.
    /// </summary>
    /// <param name="instance">Target instance.</param>
    /// <param name="methodName">Injected target method name.</param>
    [Weave("SharpWeaver.TestFixtures.Fake.MultiPatternTarget.Alpha(**)", priority: 5)]
    [Weave("SharpWeaver.TestFixtures.Fake.MultiPatternTarget.Beta(**)", priority: 5)]
    public static void MultiPatternWeave(
        object? instance,
        [WeaveMethodName] string methodName)
    {
        _ = instance;
        BehavioralState.MultiPatternLastMethodName = methodName;
        WeaveTemplate.OriginalBody();
    }

    /// <summary>
    /// Wildcard weave for non-void targets with branches converging on <c>ret</c> (similar to conversion operator).
    /// </summary>
    /// <param name="instance">Target instance.</param>
    [Weave("SharpWeaver.TestFixtures.Fake.BranchReturnTarget.Convert(**)", priority: 5)]
    public static void BranchReturnWeave(object? instance)
    {
        _ = instance;
        BehavioralState.BranchReturnBodyRuns++;
        WeaveTemplate.OriginalBody();
    }

    /// <summary>
    /// Wildcard weave for <c>init</c> property setter (return type is <c>void modreq(IsExternalInit)</c>).
    /// </summary>
    /// <param name="instance">Target instance.</param>
    [Weave("SharpWeaver.TestFixtures.Fake.InitPropertyTarget.set_Value(**)", priority: 5)]
    public static void InitPropertySetterWeave(object? instance)
    {
        _ = instance;
        BehavioralState.InitPropertySetterBodyRuns++;
        WeaveTemplate.OriginalBody();
    }

    /// <summary>
    /// Broadly matches <see cref="WildcardExcludeTarget"/>, but excludes the <c>Excluded</c> method.
    /// </summary>
    /// <param name="instance">Target instance.</param>
    [Weave("SharpWeaver.TestFixtures.Fake.WildcardExcludeTarget.*cluded(**)", priority: 5)]
    [WeaveExclude("SharpWeaver.TestFixtures.Fake.WildcardExcludeTarget.Excluded(**)")]
    public static void WildcardExcludeWeave(object? instance)
    {
        _ = instance;
        BehavioralState.WildcardExcludeWeavePrefixRuns++;
        WeaveTemplate.OriginalBody();
    }

    /// <summary>
    /// Generic-aware wildcard weave: records the method name and visible generic parameters of open generic targets.
    /// </summary>
    /// <param name="instance">Target instance.</param>
    /// <param name="genericTypeParams">Open generic parameters visible on the target method.</param>
    /// <param name="methodName">Injected target method name.</param>
    [Weave("SharpWeaver.TestFixtures.Fake.GenericMethodTarget.Echo(**)", priority: 5, genericWeave: true)]
    [Weave("SharpWeaver.TestFixtures.Fake.GenericContainer*.Run(**)", priority: 5, genericWeave: true)]
    public static void GenericCaptureWeave(
        object? instance,
        [WeaveTypeParams] Type[] genericTypeParams,
        [WeaveMethodName] string methodName)
    {
        _ = instance;
        BehavioralState.GenericWeaveRuns++;
        BehavioralState.GenericCapturedMethodName = methodName;
        var names = new string[genericTypeParams.Length];
        for (var i = 0; i < genericTypeParams.Length; i++)
        {
            names[i] = genericTypeParams[i].Name;
        }

        BehavioralState.GenericCapturedTypeParamNames = names;
        WeaveTemplate.OriginalBody();
    }

    /// <summary>
    /// Non-generic wildcard weave: matches in the same namespace as generic targets, used to verify that open generic targets are not double-woven by non-generic templates.
    /// </summary>
    /// <param name="instance">Target instance.</param>
    [Weave("SharpWeaver.TestFixtures.Fake.GenericMethodTarget.*Echo(**)", priority: 5)]
    public static void NonGenericCaptureWeave(object? instance)
    {
        _ = instance;
        BehavioralState.GenericNonGenericWeaveRuns++;
        WeaveTemplate.OriginalBody();
    }
}
