using Godot;

namespace SharpWeaver.TestFixtures.Godot;

/// <summary>Test type that inherits <see cref="Node"/> and overrides <c>_Process</c>.</summary>
public partial class GodotProcessNode : Node
{
    /// <inheritdoc />
    public override void _Process(double delta)
    {
    }
}
