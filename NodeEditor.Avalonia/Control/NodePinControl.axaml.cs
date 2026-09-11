using Avalonia;
using Avalonia.Controls.Primitives;

namespace NodeEditor.Avalonia.Control;

public class NodePinControl : TemplatedControl
{
    public static readonly StyledProperty<Guid> NodeIdProperty =
        AvaloniaProperty.Register<NodePinControl, Guid>(nameof(NodeId));

    public static readonly StyledProperty<int> IndexProperty =
        AvaloniaProperty.Register<NodePinControl, int>(nameof(Index));

    public static readonly StyledProperty<bool> IsOutputProperty =
        AvaloniaProperty.Register<NodePinControl, bool>(nameof(IsOutput));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<NodePinControl, bool>(nameof(IsActive));

    public Guid NodeId
    {
        get => GetValue(NodeIdProperty);
        set => SetValue(NodeIdProperty, value);
    }

    public int Index
    {
        get => GetValue(IndexProperty);
        set => SetValue(IndexProperty, value);
    }

    public bool IsOutput
    {
        get => GetValue(IsOutputProperty);
        set => SetValue(IsOutputProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }
}
