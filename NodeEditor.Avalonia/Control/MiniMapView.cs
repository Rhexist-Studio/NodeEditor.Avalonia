using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using NodeEditor.Avalonia.Models;
using WirePath = global::Avalonia.Controls.Shapes.Path;

namespace NodeEditor.Avalonia.Control;

public sealed class MiniMapView : Panel
{
    private static readonly IBrush BackBrush = new SolidColorBrush(Color.Parse("#E6111111"));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.Parse("#66FFFFFF")), 1);
    private static readonly IPen ViewPen = new Pen(new SolidColorBrush(Color.Parse("#FFD54F")), 1.6);
    private static readonly IBrush ViewFill = new SolidColorBrush(Color.Parse("#33FFD54F"));
    private readonly Canvas _world = new() { ClipToBounds = false, IsHitTestVisible = false };
    private readonly ViewOverlay _overlay = new() { IsHitTestVisible = false };
    private readonly Dictionary<Guid, NodeControl> _nodes = [];
    private readonly Dictionary<NodeConnection, WirePath> _wires = [];
    private Rect _content = new(0, 0, 1, 1);
    private Rect _view = new(0, 0, 1, 1);
    private double _mapScale = 1;
    private Vector _mapOffset;
    private bool _dragging;
    private bool _contentDirty = true;

    public MiniMapView()
    {
        ClipToBounds = true;
        Background = BackBrush;
        Children.Add(_world);
        Children.Add(_overlay);
        _world.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
    }

    public event Action<Point>? Navigate;

    /// <summary>
    /// 同步只读节点副本。结构或节点位置变化时重建内容，平移缩放不要调这个。
    /// </summary>
    public void SyncGraph(IReadOnlyDictionary<Guid, NodeControl> source, IReadOnlyList<NodeConnection> connections, NodeManager manager)
    {
        var stale = _nodes.Keys.Where(id => !source.ContainsKey(id)).ToList();
        foreach (var id in stale)
        {
            _world.Children.Remove(_nodes[id]);
            _nodes.Remove(id);
            _contentDirty = true;
        }

        foreach (var (id, sourceNode) in source)
        {
            if (!_nodes.TryGetValue(id, out var clone))
            {
                clone = CreateClone(sourceNode);
                _nodes[id] = clone;
                _world.Children.Add(clone);
                _contentDirty = true;
            }

            var x = Canvas.GetLeft(sourceNode);
            var y = Canvas.GetTop(sourceNode);
            if (double.IsNaN(x))
                x = 0;
            if (double.IsNaN(y))
                y = 0;
            if (Canvas.GetLeft(clone) != x || Canvas.GetTop(clone) != y)
            {
                Canvas.SetLeft(clone, x);
                Canvas.SetTop(clone, y);
                _contentDirty = true;
            }

            var w = sourceNode.Bounds.Width > 0 ? sourceNode.Bounds.Width : sourceNode.MinWidth;
            var h = sourceNode.Bounds.Height > 0 ? sourceNode.Bounds.Height : sourceNode.MinHeight;
            if (clone.Width != w || clone.Height != h)
            {
                clone.Width = w;
                clone.Height = h;
                _contentDirty = true;
            }

            if (!Equals(clone.Value, sourceNode.Value))
                clone.Value = sourceNode.Value;
        }

        SyncWires(source, connections, manager);
        if (_contentDirty)
        {
            _content = MeasureContent();
            Fit();
            _overlay.View = MapClipped(_view);
            _contentDirty = false;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// 只更新当前视口黄框，不改鸟瞰内容缩放和位置。
    /// </summary>
    public void SetView(Rect view)
    {
        _view = view;
        _overlay.View = MapClipped(view);
        _overlay.InvalidateVisual();
    }

    /// <summary>
    /// 把鸟瞰图上的点换成世界坐标。
    /// </summary>
    public Point ToWorld(Point local)
    {
        return new Point((local.X - _mapOffset.X) / _mapScale, (local.Y - _mapOffset.Y) / _mapScale);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Background = BackBrush;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _world.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _overlay.Measure(availableSize);
        return new Size(220, 140);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Fit();
        _world.Arrange(new Rect(_world.DesiredSize));
        _overlay.Arrange(new Rect(finalSize));
        _overlay.View = MapClipped(_view);
        return finalSize;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _dragging = true;
        e.Pointer.Capture(this);
        Navigate?.Invoke(ToWorld(e.GetPosition(this)));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
            return;
        Navigate?.Invoke(ToWorld(e.GetPosition(this)));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
            return;
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void SyncWires(
        IReadOnlyDictionary<Guid, NodeControl> source,
        IReadOnlyList<NodeConnection> connections,
        NodeManager manager)
    {
        var keep = new HashSet<NodeConnection>();
        foreach (var connection in connections)
        {
            if (!TryEnds(source, connection, out var start, out var end))
                continue;
            keep.Add(connection);
            if (!_wires.TryGetValue(connection, out var path))
            {
                path = new WirePath
                {
                    StrokeThickness = WireStyle.CoreThickness,
                    StrokeLineCap = PenLineCap.Round,
                    IsHitTestVisible = false
                };
                _wires[connection] = path;
                _world.Children.Insert(0, path);
                _contentDirty = true;
            }

            path.Data = WireGeometry.BuildCore(start, end);
            var from = manager.GetInstance(connection.FromNodeId)?.Definition.TitleColor ?? Colors.Gray;
            var to = manager.GetInstance(connection.ToNodeId)?.Definition.TitleColor ?? Colors.Gray;
            path.Stroke = new SolidColorBrush(Color.FromArgb(
                255,
                (byte)((from.R + to.R) / 2),
                (byte)((from.G + to.G) / 2),
                (byte)((from.B + to.B) / 2)));
        }

        foreach (var stale in _wires.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            _world.Children.Remove(_wires[stale]);
            _wires.Remove(stale);
            _contentDirty = true;
        }
    }

    private static bool TryEnds(IReadOnlyDictionary<Guid, NodeControl> source, NodeConnection connection, out Point start, out Point end)
    {
        start = default;
        end = default;
        if (!source.TryGetValue(connection.FromNodeId, out var fromNode))
            return false;
        if (!source.TryGetValue(connection.ToNodeId, out var toNode))
            return false;
        var fromPin = fromNode.GetPin(connection.FromOutputIndex, true);
        var toPin = toNode.GetPin(connection.ToInputIndex, false);
        if (fromPin == null || toPin == null)
            return false;
        var from = fromPin.TranslatePoint(new Point(fromPin.Bounds.Width / 2, fromPin.Bounds.Height / 2), fromNode);
        var to = toPin.TranslatePoint(new Point(toPin.Bounds.Width / 2, toPin.Bounds.Height / 2), toNode);
        if (from == null || to == null)
            return false;
        start = NodeOrigin(fromNode) + from.Value;
        end = NodeOrigin(toNode) + to.Value;
        return true;
    }

    private static Point NodeOrigin(NodeControl node)
    {
        var x = Canvas.GetLeft(node);
        var y = Canvas.GetTop(node);
        if (double.IsNaN(x))
            x = 0;
        if (double.IsNaN(y))
            y = 0;
        return new Point(x, y);
    }

    private static NodeControl CreateClone(NodeControl source)
    {
        var clone = new NodeControl
        {
            IsReadOnly = true,
            IsHitTestVisible = false,
            Focusable = false
        };
        if (source.Instance != null)
            clone.BindInstance(source.Instance);
        clone.Width = source.Bounds.Width > 0 ? source.Bounds.Width : source.MinWidth;
        clone.Height = source.Bounds.Height > 0 ? source.Bounds.Height : source.MinHeight;
        return clone;
    }

    private Rect MeasureContent()
    {
        Rect? bounds = null;
        foreach (var node in _nodes.Values)
        {
            var origin = NodeOrigin(node);
            var w = node.Width > 0 ? node.Width : Math.Max(node.Bounds.Width, node.MinWidth);
            var h = node.Height > 0 ? node.Height : Math.Max(node.Bounds.Height, node.MinHeight);
            var rect = new Rect(origin.X, origin.Y, Math.Max(w, 1), Math.Max(h, 1));
            bounds = bounds == null ? rect : bounds.Value.Union(rect);
        }

        return bounds ?? new Rect(0, 0, 1, 1);
    }

    private void Fit()
    {
        var w = Math.Max(_content.Width, 1);
        var h = Math.Max(_content.Height, 1);
        var bw = Bounds.Width > 0 ? Bounds.Width : 220;
        var bh = Bounds.Height > 0 ? Bounds.Height : 140;
        _mapScale = Math.Min(bw / w, bh / h);
        if (_mapScale <= 0 || double.IsNaN(_mapScale) || double.IsInfinity(_mapScale))
            _mapScale = 1;
        var used = new Size(w * _mapScale, h * _mapScale);
        _mapOffset = new Vector(
            (bw - used.Width) / 2 - _content.X * _mapScale,
            (bh - used.Height) / 2 - _content.Y * _mapScale);
        _world.RenderTransform = new MatrixTransform(new Matrix(_mapScale, 0, 0, _mapScale, _mapOffset.X, _mapOffset.Y));
    }

    private Rect MapClipped(Rect world)
    {
        var mapped = new Rect(
            world.X * _mapScale + _mapOffset.X,
            world.Y * _mapScale + _mapOffset.Y,
            world.Width * _mapScale,
            world.Height * _mapScale);
        var area = new Rect(Bounds.Size);
        return mapped.Intersect(area);
    }

    private sealed class ViewOverlay : global::Avalonia.Controls.Control
    {
        public Rect View { get; set; }

        public override void Render(DrawingContext context)
        {
            if (View.Width <= 0 || View.Height <= 0)
                return;
            context.FillRectangle(ViewFill, View);
            context.DrawRectangle(ViewPen, View);
        }
    }
}
