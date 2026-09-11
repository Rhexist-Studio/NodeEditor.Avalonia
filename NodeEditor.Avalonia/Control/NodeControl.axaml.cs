using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using NodeEditor.Avalonia.Models;

namespace NodeEditor.Avalonia.Control;

public class NodeControl : TemplatedControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<NodeControl, string>(nameof(Title), "");

    public static readonly StyledProperty<IBrush> TitleBrushProperty =
        AvaloniaProperty.Register<NodeControl, IBrush>(nameof(TitleBrush), Brushes.DarkRed);

    public static readonly StyledProperty<IEnumerable> InputsProperty =
        AvaloniaProperty.Register<NodeControl, IEnumerable>(nameof(Inputs), Array.Empty<NodePinView>());

    public static readonly StyledProperty<IEnumerable> OutputsProperty =
        AvaloniaProperty.Register<NodeControl, IEnumerable>(nameof(Outputs), Array.Empty<NodePinView>());

    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<NodeControl, bool>(nameof(IsSelected));

    public static readonly DirectProperty<NodeControl, Guid> InstanceIdProperty =
        AvaloniaProperty.RegisterDirect<NodeControl, Guid>(nameof(InstanceId), o => o.InstanceId);

    private Border? _titleBar;
    private Guid _instanceId;

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public IBrush TitleBrush
    {
        get => GetValue(TitleBrushProperty);
        set => SetValue(TitleBrushProperty, value);
    }

    public IEnumerable Inputs
    {
        get => GetValue(InputsProperty);
        set => SetValue(InputsProperty, value);
    }

    public IEnumerable Outputs
    {
        get => GetValue(OutputsProperty);
        set => SetValue(OutputsProperty, value);
    }

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public Guid InstanceId
    {
        get => _instanceId;
        private set => SetAndRaise(InstanceIdProperty, ref _instanceId, value);
    }

    /// <summary>
    /// 用节点实例填充标题、颜色和输入输出端点。
    /// </summary>
    public void BindInstance(NodeInstance instance)
    {
        InstanceId = instance.Id;
        Title = instance.Definition.Title;
        TitleBrush = new SolidColorBrush(instance.Definition.TitleColor);
        Inputs = instance.Definition.Inputs
            .Select(p => new NodePinView(instance.Id, p.Index, p.Name, false))
            .ToList();
        Outputs = instance.Definition.Outputs
            .Select(p => new NodePinView(instance.Id, p.Index, p.Name, true))
            .ToList();
    }

    /// <summary>
    /// 判断指针源是否点在标题栏上，用于拖动画布节点。
    /// </summary>
    public bool IsHeaderSource(Visual? source)
    {
        if (_titleBar == null || source == null)
            return false;
        return source == _titleBar || _titleBar.IsVisualAncestorOf(source);
    }

    /// <summary>
    /// 按端点序号取输入或输出图钉控件，找不到返回 null。
    /// </summary>
    public NodePinControl? GetPin(int index, bool isOutput)
    {
        return this.GetVisualDescendants()
            .OfType<NodePinControl>()
            .FirstOrDefault(p => p.Index == index && p.IsOutput == isOutput);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _titleBar = e.NameScope.Find<Border>("PART_TitleBar");
    }
}
