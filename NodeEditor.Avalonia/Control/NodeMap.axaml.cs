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
    private readonly HashSet<NodeControl> _selected = [];
    private readonly Dictionary<NodeControl, Point> _dragOrigins = [];
    private NodeControl? _dragging;
    private Point _dragPointerStart;
    private bool _panning;
    private bool _marquee;
    private Point _marqueeStart;
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
        MiniMap.Navigate += OnMiniMapNavigate;
        AttachedToVisualTree += (_, _) =>
        {
            RedrawConnections();
            UpdateMiniMapContent();
            UpdateMiniMapView();
        };
        SizeChanged += (_, _) => UpdateMiniMapView();
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

    /// <summary>
    /// 用 Export JSON 还原节点视图，会先清空当前画布。
    /// </summary>
    public void ImportJson(string json)
    {
        CancelPending();
        ClearSelection();
        Manager.ImportJson(json);
        Dispatcher.UIThread.Post(() =>
        {
            RedrawConnections();
            UpdateMiniMapContent();
            UpdateMiniMapView();
        }, DispatcherPriority.Loaded);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (FindAncestor<MiniMapView>(e.Source) != null)
            return;
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
                SelectOnly(pinNode);
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
            if (!_selected.Contains(node))
                SelectOnly(node);
            if (IsEditorSource(e.Source))
                return;
            if (props.IsLeftButtonPressed && node.IsHeaderSource(e.Source as Visual))
            {
                BeginDrag(node, point);
                e.Pointer.Capture(this);
            }

            e.Handled = true;
            return;
        }

        if (props.IsRightButtonPressed)
        {
            CancelPending();
            ClearSelection();
            OpenCreateMenu(ToWorld(point));
            e.Handled = true;
            return;
        }

        if (props.IsMiddleButtonPressed)
        {
            CancelPending();
            _panning = true;
            _panStart = point;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            CancelPending();
            ClearSelection();
            _marquee = true;
            _marqueeStart = point;
            UpdateMarquee(point);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (FindAncestor<MiniMapView>(e.Source) != null && _dragging == null && !_panning && !_marquee && _pendingPin == null)
            return;
        var point = e.GetPosition(this);
        if (_pendingPin != null)
        {
            UpdateTempPath(point);
            return;
        }

        if (_dragging != null)
        {
            MoveSelection(point);
            return;
        }

        if (_panning)
        {
            _offset += point - _panStart;
            _panStart = point;
            ApplyTransform();
            return;
        }

        if (_marquee)
        {
            UpdateMarquee(point);
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
            foreach (var node in _selected)
                node.ZIndex = 0;
            _dragging = null;
            _dragOrigins.Clear();
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_panning)
        {
            _panning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_marquee)
        {
            ApplyMarquee(point);
            _marquee = false;
            SelectionBox.IsVisible = false;
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
        if (e.Key == Key.Delete && _selected.Count > 0)
        {
            foreach (var id in _selected.Select(n => n.InstanceId).ToList())
                Manager.Remove(id);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelPending();
            ClearSelection();
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
        Dispatcher.UIThread.Post(() =>
        {
            RedrawConnections();
            UpdateMiniMapContent();
            UpdateMiniMapView();
        }, DispatcherPriority.Loaded);
    }

    private void OnNodeRemoved(NodeInstance instance)
    {
        if (!_controls.Remove(instance.Id, out var control))
            return;
        NodeLayer.Children.Remove(control);
        _selected.Remove(control);
        UpdateMiniMapContent();
        UpdateMiniMapView();
    }

    private void SelectOnly(NodeControl control)
    {
        ClearSelection();
        _selected.Add(control);
        control.IsSelected = true;
    }

    private void ClearSelection()
    {
        foreach (var node in _selected)
            node.IsSelected = false;
        _selected.Clear();
    }

    private void BeginDrag(NodeControl node, Point point)
    {
        _dragging = node;
        _dragPointerStart = point;
        _dragOrigins.Clear();
        foreach (var selected in _selected)
        {
            selected.ZIndex = _controls.Count + 1;
            _dragOrigins[selected] = new Point(Canvas.GetLeft(selected), Canvas.GetTop(selected));
        }
    }

    private void MoveSelection(Point point)
    {
        var worldDelta = (point - _dragPointerStart) / _scale;
        foreach (var (node, origin) in _dragOrigins)
        {
            var x = origin.X + worldDelta.X;
            var y = origin.Y + worldDelta.Y;
            Canvas.SetLeft(node, x);
            Canvas.SetTop(node, y);
            var instance = Manager.GetInstance(node.InstanceId);
            if (instance == null)
                continue;
            instance.X = x;
            instance.Y = y;
        }

        UpdateWireGeometries();
        UpdateMiniMapContent();
        UpdateMiniMapView();
    }

    private void UpdateMarquee(Point point)
    {
        var x = Math.Min(_marqueeStart.X, point.X);
        var y = Math.Min(_marqueeStart.Y, point.Y);
        var w = Math.Abs(point.X - _marqueeStart.X);
        var h = Math.Abs(point.Y - _marqueeStart.Y);
        Canvas.SetLeft(SelectionBox, x);
        Canvas.SetTop(SelectionBox, y);
        SelectionBox.Width = w;
        SelectionBox.Height = h;
        SelectionBox.IsVisible = w > 2 || h > 2;
    }

    private void ApplyMarquee(Point point)
    {
        var a = ToWorld(_marqueeStart);
        var b = ToWorld(point);
        var box = new Rect(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Max(Math.Abs(b.X - a.X), 1),
            Math.Max(Math.Abs(b.Y - a.Y), 1));
        ClearSelection();
        foreach (var node in _controls.Values)
        {
            if (!box.Intersects(NodeWorldRect(node)))
                continue;
            _selected.Add(node);
            node.IsSelected = true;
        }
    }

    private static Rect NodeWorldRect(NodeControl node)
    {
        var x = Canvas.GetLeft(node);
        var y = Canvas.GetTop(node);
        if (double.IsNaN(x))
            x = 0;
        if (double.IsNaN(y))
            y = 0;
        var w = node.Bounds.Width > 0 ? node.Bounds.Width : node.MinWidth;
        var h = node.Bounds.Height > 0 ? node.Bounds.Height : node.MinHeight;
        return new Rect(x, y, Math.Max(w, 1), Math.Max(h, 1));
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
            if (!TryGetWireVisual(connection, out var start, out var end, out var fromColor, out var toColor))
                continue;
            keep.Add(connection);
            if (!_wires.TryGetValue(connection, out var wire))
            {
                wire = new ConnectionWire(connection);
                _wires[connection] = wire;
                ConnectionLayer.Children.Insert(0, wire);
            }
            wire.SetGeometry(start, end, fromColor, toColor);
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
        UpdateMiniMapContent();
    }

    private void UpdateWireGeometries()
    {
        foreach (var (connection, wire) in _wires)
        {
            if (TryGetWireVisual(connection, out var start, out var end, out var fromColor, out var toColor))
                wire.SetGeometry(start, end, fromColor, toColor);
        }
    }

    private bool TryGetWireVisual(
        NodeConnection connection,
        out Point start,
        out Point end,
        out Color fromColor,
        out Color toColor)
    {
        start = default;
        end = default;
        fromColor = Colors.Gray;
        toColor = Colors.Gray;
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
        var fromInstance = Manager.GetInstance(connection.FromNodeId);
        var toInstance = Manager.GetInstance(connection.ToNodeId);
        if (fromInstance != null)
            fromColor = fromInstance.Definition.TitleColor;
        if (toInstance != null)
            toColor = toInstance.Definition.TitleColor;
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
        var data = new MenuItem { Header = "Data" };
        var nodes = new MenuItem { Header = "Nodes" };
        foreach (var definition in Manager.Definitions)
        {
            var item = new MenuItem { Header = definition.Title };
            var name = definition.Name;
            item.Click += (_, _) => Manager.Create(name, world.X, world.Y);
            if (definition.ValueKind != null)
                data.Items.Add(item);
            else
                nodes.Items.Add(item);
        }

        if (data.Items.Count > 0)
            menu.Items.Add(data);
        if (nodes.Items.Count > 0)
            menu.Items.Add(nodes);
        if (menu.Items.Count == 0)
            return;
        menu.Open(this);
    }

    private void ApplyTransform()
    {
        World.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        World.RenderTransform = new MatrixTransform(new Matrix(_scale, 0, 0, _scale, _offset.X, _offset.Y));
        UpdateMiniMapView();
    }

    private void UpdateMiniMapContent()
    {
        MiniMap?.SyncGraph(_controls, Manager.Connections, Manager);
    }

    private void UpdateMiniMapView()
    {
        if (MiniMap == null)
            return;
        MiniMap.SetView(new Rect(
            -_offset.X / _scale,
            -_offset.Y / _scale,
            Math.Max(Bounds.Width / _scale, 1),
            Math.Max(Bounds.Height / _scale, 1)));
    }

    private void OnMiniMapNavigate(Point worldCenter)
    {
        _offset = new Vector(
            Bounds.Width / 2 - worldCenter.X * _scale,
            Bounds.Height / 2 - worldCenter.Y * _scale);
        ApplyTransform();
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

    private static bool IsEditorSource(object? source)
    {
        return FindAncestor<TextBox>(source) != null || FindAncestor<CheckBox>(source) != null;
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
