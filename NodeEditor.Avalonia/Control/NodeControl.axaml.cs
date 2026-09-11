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

    public static readonly StyledProperty<bool> IsDataSourceProperty =
        AvaloniaProperty.Register<NodeControl, bool>(nameof(IsDataSource));

    public static readonly StyledProperty<NodeValueKind> ValueKindProperty =
        AvaloniaProperty.Register<NodeControl, NodeValueKind>(nameof(ValueKind));

    public static readonly StyledProperty<object?> ValueProperty =
        AvaloniaProperty.Register<NodeControl, object?>(nameof(Value));

    public static readonly DirectProperty<NodeControl, Guid> InstanceIdProperty =
        AvaloniaProperty.RegisterDirect<NodeControl, Guid>(nameof(InstanceId), o => o.InstanceId);

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<NodeControl, bool>(nameof(IsReadOnly));

    private Border? _titleBar;
    private ContentControl? _valueHost;
    private NodeInstance? _instance;
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

    public bool IsDataSource
    {
        get => GetValue(IsDataSourceProperty);
        set => SetValue(IsDataSourceProperty, value);
    }

    public NodeValueKind ValueKind
    {
        get => GetValue(ValueKindProperty);
        set => SetValue(ValueKindProperty, value);
    }

    public object? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public NodeInstance? Instance => _instance;

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
        _instance = instance;
        InstanceId = instance.Id;
        Title = instance.Definition.Title;
        TitleBrush = new SolidColorBrush(instance.Definition.TitleColor);
        var data = instance.Definition.ValueKind != null;
        Inputs = instance.Definition.Inputs
            .Select(p => new NodePinView(instance.Id, p.Index, p.Name, false))
            .ToList();
        Outputs = instance.Definition.Outputs
            .Select(p => new NodePinView(instance.Id, p.Index, p.Name, true, !data))
            .ToList();
        IsDataSource = instance.Definition.ValueKind != null;
        ValueKind = instance.Definition.ValueKind ?? NodeValueKind.String;
        Value = instance.Value;
        AttachEditor();
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
        _valueHost = e.NameScope.Find<ContentControl>("PART_ValueHost");
        AttachEditor();
    }

    private void AttachEditor()
    {
        if (_valueHost == null || _instance?.Definition.ValueKind == null)
            return;
        _valueHost.Content = IsReadOnly
            ? CreateReadOnlyValue(_instance.Definition.ValueKind.Value, _instance.Value)
            : CreateEditor(_instance.Definition.ValueKind.Value, _instance.Value);
    }

    private static TextBlock CreateReadOnlyValue(NodeValueKind kind, object? value)
    {
        return new TextBlock
        {
            Text = kind == NodeValueKind.Bool
                ? (value is true ? "true" : "false")
                : value?.ToString() ?? "",
            Foreground = Brushes.White,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
        };
    }

    private global::Avalonia.Controls.Control CreateEditor(NodeValueKind kind, object? value)
    {
        return kind switch
        {
            NodeValueKind.Bool => CreateBoolEditor(value),
            NodeValueKind.String => CreateTextEditor(value, false),
            _ => CreateTextEditor(value, true)
        };
    }

    private CheckBox CreateBoolEditor(object? value)
    {
        var box = new CheckBox
        {
            Content = "Value",
            IsChecked = value is true,
            Foreground = Brushes.White
        };
        box.IsCheckedChanged += (_, _) => Commit(box.IsChecked == true);
        return box;
    }

    private TextBox CreateTextEditor(object? value, bool numeric)
    {
        var box = new TextBox
        {
            Text = value?.ToString() ?? "",
            MinHeight = 28,
            Padding = new Thickness(6, 2)
        };
        box.LostFocus += (_, _) => CommitText(box.Text, numeric);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == global::Avalonia.Input.Key.Enter)
            {
                CommitText(box.Text, numeric);
                e.Handled = true;
            }
        };
        return box;
    }

    private void CommitText(string? text, bool numeric)
    {
        if (_instance?.Definition.ValueKind == null)
            return;
        if (!numeric)
        {
            Commit(text ?? "");
            return;
        }

        Commit(Parse(_instance.Definition.ValueKind.Value, text));
    }

    private void Commit(object? value)
    {
        if (_instance == null)
            return;
        _instance.Value = value;
        Value = value;
    }

    private static object Parse(NodeValueKind kind, string? text)
    {
        text ??= "";
        return kind switch
        {
            NodeValueKind.Int when int.TryParse(text, out var i) => i,
            NodeValueKind.Long when long.TryParse(text, out var l) => l,
            NodeValueKind.Float when float.TryParse(text, out var f) => f,
            NodeValueKind.Double when double.TryParse(text, out var d) => d,
            NodeValueKind.Int => 0,
            NodeValueKind.Long => 0L,
            NodeValueKind.Float => 0f,
            NodeValueKind.Double => 0d,
            _ => text
        };
    }
}
