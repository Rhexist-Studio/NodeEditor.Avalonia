using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NodeEditor.Avalonia.Models;
using WirePath = global::Avalonia.Controls.Shapes.Path;

namespace NodeEditor.Avalonia.Control;

public partial class NodeMap : UserControl
{
    private readonly Dictionary<Guid, NodeControl> _controls = [];
    private readonly Dictionary<NodeConnection, ConnectionWire> _wires = [];
    private ConnectionWire? _hoveredWire;
    private NodeControl? _selected;
    private NodeControl? _dragging;
    private Point _dragPointerStart;
    private double _dragNodeStartX;
    private double _dragNodeStartY;
    private bool _panning;
    private Point _panStart;
    private Vector _offset;
    private double _scale = 1;
    private NodePinControl? _pendingPin;
    private WirePath? _tempPath;
    private bool _subscribed;

    public NodeMap()
    {
        Manager = new NodeManager();
        InitializeComponent();
        Subscribe();
        ApplyTransform();
        AttachedToVisualTree += (_, _) => RedrawConnections();
    }

    public NodeManager Manager { get; }

    /// <summary>
    /// 在画布世界坐标 (x, y) 创建已注册类型的节点。typeName 可以是 SendMessage 或 node:sendMessage。
    /// </summary>
    public NodeInstance CreateNode(string typeName, double x, double y)
    {
        return Manager.Create(typeName, x, y);
    }

    /// <summary>
    /// 导出当前画布节点列表，格式与 NodeManager.Export 相同。
    /// </summary>
    public IReadOnlyList<ExportedNode> Export()
    {
        return Manager.Export();
    }

    /// <summary>
    /// 导出当前画布为 JSON 字符串。
    /// </summary>
    public string ExportJson()
    {
        return Manager.ExportJson();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        if (FindPin(e.Source, out var pin, out var pinNode))
        {
            if (props.IsRightButtonPressed)
            {
                Manager.Disconnect(pin.NodeId, pin.Index, pin.IsOutput);
                e.Handled = true;
                return;
            }

            if (props.IsLeftButtonPressed)
            {
                Select(pinNode);
                HandlePinPressed(pin);
                if (_pendingPin != null)
                    e.Pointer.Capture(this);
                e.Handled = true;
            }

            return;
        }

        if (FindPath(e.Source, out var connection) && props.IsRightButtonPressed)
        {
            Manager.Disconnect(connection);
            e.Handled = true;
            return;
        }

        if (FindNode(e.Source, out var node))
        {
            CancelPending();
            Select(node);
            if (props.IsLeftButtonPressed && node.IsHeaderSource(e.Source as Visual))
            {
                _dragging = node;
                _dragPointerStart = point;
                _dragNodeStartX = Canvas.GetLeft(node);
                _dragNodeStartY = Canvas.GetTop(node);
                node.ZIndex = _controls.Count + 1;
                e.Pointer.Capture(this);
            }

            e.Handled = true;
            return;
        }

        if (props.IsRightButtonPressed)
        {
            CancelPending();
            Select(null);
            OpenCreateMenu(ToWorld(point));
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed || props.IsMiddleButtonPressed)
        {
            CancelPending();
            Select(null);
            _panning = true;
            _panStart = point;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        if (_pendingPin != null)
        {
            UpdateTempPath(point);
            return;
        }

        if (_dragging != null)
        {
            var worldDelta = (point - _dragPointerStart) / _scale;
            var x = _dragNodeStartX + worldDelta.X;
            var y = _dragNodeStartY + worldDelta.Y;
            Canvas.SetLeft(_dragging, x);
            Canvas.SetTop(_dragging, y);
            var instance = Manager.GetInstance(_dragging.InstanceId);
            if (instance != null)
            {
                instance.X = x;
                instance.Y = y;
            }

            UpdateWireGeometries();
            return;
        }

        if (_panning)
        {
            _offset += point - _panStart;
            _panStart = point;
            ApplyTransform();
            return;
        }

        UpdateHover(e.Source);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var point = e.GetPosition(this);
        if (_pendingPin != null)
        {
            if (FindPinAt(point, out var pin, out _) && pin != _pendingPin)
            {
                if (CanConnect(_pendingPin, pin))
                    TryConnect(_pendingPin, pin);
                CancelPending();
            }
            else if (!FindPinAt(point, out _, out _))
                CancelPending();
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_dragging != null)
        {
            _dragging.ZIndex = 0;
            _dragging = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_panning)
        {
            _panning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoveredWire != null)
        {
            _hoveredWire.SetHovered(false);
            _hoveredWire = null;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var screen = e.GetPosition(this);
        var world = ToWorld(screen);
        var factor = e.Delta.Y > 0 ? 1.1 : 0.9;
        _scale = Math.Clamp(_scale * factor, 0.3, 2.5);
        _offset = new Vector(screen.X - world.X * _scale, screen.Y - world.Y * _scale);
        ApplyTransform();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Delete && _selected != null)
        {
            Manager.Remove(_selected.InstanceId);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelPending();
            Select(null);
            e.Handled = true;
        }
    }

    private void Subscribe()
    {
        if (_subscribed)
            return;
        _subscribed = true;
        Manager.NodeCreated += OnNodeCreated;
        Manager.NodeRemoved += OnNodeRemoved;
        Manager.Changed += RedrawConnections;
    }

    private void OnNodeCreated(NodeInstance instance)
    {
        var control = new NodeControl();
        control.BindInstance(instance);
        Canvas.SetLeft(control, instance.X);
        Canvas.SetTop(control, instance.Y);
        NodeLayer.Children.Add(control);
        _controls[instance.Id] = control;
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (control.Bounds.Width <= 0)
                return;
            control.LayoutUpdated -= handler;
            RedrawConnections();
        };
        control.LayoutUpdated += handler;
        Dispatcher.UIThread.Post(RedrawConnections, DispatcherPriority.Loaded);
    }

    private void OnNodeRemoved(NodeInstance instance)
    {
        if (!_controls.Remove(instance.Id, out var control))
            return;
        NodeLayer.Children.Remove(control);
        if (_selected == control)
            _selected = null;
    }

    private void Select(NodeControl? control)
    {
        if (_selected != null)
            _selected.IsSelected = false;
        _selected = control;
        if (_selected != null)
            _selected.IsSelected = true;
    }

    private void HandlePinPressed(NodePinControl pin)
    {
        if (_pendingPin == null)
        {
            _pendingPin = pin;
            pin.IsActive = true;
            EnsureTempPath();
            return;
        }

        if (CanConnect(_pendingPin, pin))
        {
            TryConnect(_pendingPin, pin);
            CancelPending();
        }
        else if (pin != _pendingPin)
        {
            _pendingPin.IsActive = false;
            _pendingPin = pin;
            pin.IsActive = true;
            EnsureTempPath();
        }
    }

    private void TryConnect(NodePinControl a, NodePinControl b)
    {
        var from = a.IsOutput ? a : b;
        var to = a.IsOutput ? b : a;
        Manager.Connect(from.NodeId, from.Index, to.NodeId, to.Index);
    }

    private static bool CanConnect(NodePinControl a, NodePinControl b)
    {
        return a.NodeId != b.NodeId && a.IsOutput != b.IsOutput;
    }

    private void CancelPending()
    {
        if (_pendingPin != null)
        {
            _pendingPin.IsActive = false;
            _pendingPin = null;
        }

        if (_tempPath != null)
        {
            ConnectionLayer.Children.Remove(_tempPath);
            _tempPath = null;
        }
    }

    private void EnsureTempPath()
    {
        if (_tempPath != null)
            return;
        _tempPath = new WirePath
        {
            Stroke = new SolidColorBrush(Color.Parse("#FFD54F")),
            StrokeThickness = WireStyle.CoreThickness,
            IsHitTestVisible = false
        };
        ConnectionLayer.Children.Add(_tempPath);
    }

    private void UpdateTempPath(Point screenPoint)
    {
        if (_pendingPin == null || _tempPath == null)
            return;
        var start = GetPinWorldPoint(_pendingPin);
        if (start == null)
            return;
        var end = ToWorld(screenPoint);
        if (!_pendingPin.IsOutput)
            (start, end) = (end, start.Value);
        _tempPath.Data = WireGeometry.BuildCore(start.Value, end);
    }

    private void RedrawConnections()
    {
        if (ConnectionLayer == null)
            return;
        var keep = new HashSet<NodeConnection>();
        foreach (var connection in Manager.Connections)
        {
            if (!TryGetWireEnds(connection, out var start, out var end))
                continue;
            keep.Add(connection);
            if (!_wires.TryGetValue(connection, out var wire))
            {
                wire = new ConnectionWire(connection);
                _wires[connection] = wire;
                ConnectionLayer.Children.Insert(0, wire);
            }
            wire.SetGeometry(start, end);
        }

        foreach (var stale in _wires.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            ConnectionLayer.Children.Remove(_wires[stale]);
            _wires.Remove(stale);
            if (_hoveredWire != null && stale.Equals(_hoveredWire.Connection))
                _hoveredWire = null;
        }

        if (_tempPath != null && !ConnectionLayer.Children.Contains(_tempPath))
            ConnectionLayer.Children.Add(_tempPath);
    }

    private void UpdateWireGeometries()
    {
        foreach (var (connection, wire) in _wires)
        {
            if (TryGetWireEnds(connection, out var start, out var end))
                wire.SetGeometry(start, end);
        }
    }

    private bool TryGetWireEnds(NodeConnection connection, out Point start, out Point end)
    {
        start = default;
        end = default;
        if (!_controls.TryGetValue(connection.FromNodeId, out var fromNode))
            return false;
        if (!_controls.TryGetValue(connection.ToNodeId, out var toNode))
            return false;
        var fromPin = fromNode.GetPin(connection.FromOutputIndex, true);
        var toPin = toNode.GetPin(connection.ToInputIndex, false);
        if (fromPin == null || toPin == null)
            return false;
        var from = GetPinWorldPoint(fromPin);
        var to = GetPinWorldPoint(toPin);
        if (from == null || to == null)
            return false;
        start = from.Value;
        end = to.Value;
        return true;
    }

    private void UpdateHover(object? source)
    {
        FindPath(source, out var connection);
        ConnectionWire? next = null;
        if (connection != null)
            _wires.TryGetValue(connection, out next);
        if (_hoveredWire == next)
            return;
        _hoveredWire?.SetHovered(false);
        _hoveredWire = next;
        _hoveredWire?.SetHovered(true);
    }

    private void OpenCreateMenu(Point world)
    {
        var menu = new ContextMenu { Placement = PlacementMode.Pointer };
        foreach (var definition in Manager.Definitions)
        {
            var item = new MenuItem { Header = definition.Title };
            var name = definition.Name;
            item.Click += (_, _) => Manager.Create(name, world.X, world.Y);
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
            return;
        menu.Open(this);
    }

    private void ApplyTransform()
    {
        World.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        World.RenderTransform = new MatrixTransform(new Matrix(_scale, 0, 0, _scale, _offset.X, _offset.Y));
    }

    private Point ToWorld(Point screen)
    {
        var mapped = this.TranslatePoint(screen, World);
        if (mapped != null)
            return mapped.Value;
        return new Point((screen.X - _offset.X) / _scale, (screen.Y - _offset.Y) / _scale);
    }

    private Point? GetPinWorldPoint(NodePinControl pin)
    {
        var local = new Point(pin.Bounds.Width / 2, pin.Bounds.Height / 2);
        return pin.TranslatePoint(local, World);
    }

    private bool FindPinAt(Point screenPoint, out NodePinControl pin, out NodeControl node)
    {
        pin = null!;
        node = null!;
        var hit = this.InputHitTest(screenPoint);
        return FindPin(hit, out pin, out node);
    }

    private static bool FindPin(object? source, out NodePinControl pin, out NodeControl node)
    {
        pin = FindAncestor<NodePinControl>(source)!;
        node = FindAncestor<NodeControl>(source)!;
        return pin != null && node != null;
    }

    private static bool FindNode(object? source, out NodeControl node)
    {
        node = FindAncestor<NodeControl>(source)!;
        return node != null;
    }

    private bool FindPath(object? source, out NodeConnection connection)
    {
        var wire = FindAncestor<ConnectionWire>(source);
        connection = wire?.Connection!;
        return wire != null;
    }

    private static T? FindAncestor<T>(object? source) where T : class
    {
        for (var visual = source as Visual; visual != null; visual = visual.GetVisualParent())
        {
            if (visual is T match)
                return match;
        }

        return null;
    }
}
