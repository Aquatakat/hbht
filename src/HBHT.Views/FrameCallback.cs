using Godot;

namespace HBHT.Views;

/// <summary>Calls a function every frame while it's in the tree, for classes that aren't nodes.</summary>
public partial class FrameCallback : Node
{
    private readonly Action<double>? _update;

    public FrameCallback()
    {
    }

    public FrameCallback(Action<double> update) => _update = update;

    public override void _Process(double delta) => _update?.Invoke(delta);
}
