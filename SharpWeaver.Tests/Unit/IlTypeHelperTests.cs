using SharpWeaver;
using Mono.Cecil;
using Xunit;

namespace SharpWeaver.Tests;

/// <summary><see cref="IlTypeHelper"/> unit tests.</summary>
[TestProgress]
public class IlTypeHelperTests
{
    /// <summary>
    /// <c>void modreq(IsExternalInit)</c> on an <c>init</c> setter should be treated as void return.
    /// </summary>
    [Fact]
    public void IsVoidReturn_treats_void_modreq_as_void()
    {
        var fixturesPath = Path.Combine(AppContext.BaseDirectory, "SharpWeaver.TestFixtures.dll");
        var assembly = AssemblyDefinition.ReadAssembly(fixturesPath);
        var setter = assembly.MainModule.EnumerateAllTypes()
            .First(t => t.FullName == "SharpWeaver.TestFixtures.Fake.InitPropertyTarget")
            .Methods.First(m => m.Name == "set_Value");

        Assert.Equal(MetadataType.RequiredModifier, setter.ReturnType.MetadataType);
        Assert.True(IlTypeHelper.IsVoidReturn(setter.ReturnType));
    }
}
