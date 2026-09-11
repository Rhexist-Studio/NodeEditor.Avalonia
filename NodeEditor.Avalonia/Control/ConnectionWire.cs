using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using NodeEditor.Avalonia.Models;
using WirePath = global::Avalonia.Controls.Shapes.Path;
using WireShape = global::Avalonia.Controls.Shapes.Shape;

namespace NodeEditor.Avalonia.Control;

public sealed class ConnectionWire : Panel
{
    private static readonly IBrush CoreBrush = new SolidColorBrush(Color.Parse("#C8C8C8"));
    private static readonly IBrush GlowBrush = new SolidColorBrush(Color.Parse("#FFD54F"));
    private readonly WirePath _glow;
    private readonly WirePath _core;
    private readonly WirePath _hit;
    private CancellationTokenSource? _dash;

    public ConnectionWire(NodeConnection connection)
    {
        Connection = connection;
        ClipToBounds = false;
        UseLayoutRounding = false;
        IsHitTestVisible = true;
        _glow = new WirePath
        {
            Stroke = GlowBrush,
            StrokeThickness = WireStyle.OutlineThickness,
            StrokeDashArray = [WireStyle.OutlineDash, WireStyle.OutlineGap],
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            IsVisible = false,
            IsHitTestVisible = false,
            UseLayoutRounding = false
        };
        _core = new WirePath
        {
            Stroke = CoreBrush,
            StrokeThickness = WireStyle.CoreThickness,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
            UseLayoutRounding = false
        };
        _hit = new WirePath
        {
            Stroke = Brushes.Transparent,
            StrokeThickness = WireStyle.OutlineRadius * 2 + 4,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round
        };
        Children.Add(_glow);
        Children.Add(_core);
        Children.Add(_hit);
    }

    public NodeConnection Connection { get; }

    /// <summary>
    /// 按连线两端点更新核心线和绕线一圈的闭合虚线轮廓。
    /// </summary>
    public void SetGeometry(Point start, Point end)
    {
        _core.Data = WireGeometry.BuildCore(start, end);
        _hit.Data = WireGeometry.BuildCore(start, end);
        _glow.Data = WireGeometry.BuildOutline(start, end, WireStyle.OutlineRadius);
        InvalidateMeasure();
    }

    /// <summary>
    /// 开关悬浮高亮：虚线沿胶囊轮廓绕核心线走动。
    /// </summary>
    public void SetHovered(bool hovered)
    {
        _glow.IsVisible = hovered;
        Cursor = hovered
            ? new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand)
            : global::Avalonia.Input.Cursor.Default;
        StopDash();
        if (!hovered)
        {
            _glow.StrokeDashOffset = 0;
            return;
        }

        var animation = new Animation
        {
            Duration = TimeSpan.FromSeconds(WireStyle.OutlinePeriod),
            IterationCount = IterationCount.Infinite,
            Easing = new LinearEasing(),
            FillMode = FillMode.None,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(WireShape.StrokeDashOffsetProperty, 0d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(WireShape.StrokeDashOffsetProperty, WireStyle.OutlineCycle) }
                }
            }
        };
        _dash = new CancellationTokenSource();
        _ = animation.RunAsync(_glow, _dash.Token);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size();
        foreach (var child in Children)
        {
            child.Measure(availableSize);
            size = new Size(
                Math.Max(size.Width, child.DesiredSize.Width),
                Math.Max(size.Height, child.DesiredSize.Height));
        }

        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
            child.Arrange(new Rect(child.DesiredSize));
        return finalSize;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopDash();
        base.OnDetachedFromVisualTree(e);
    }

    private void StopDash()
    {
        _dash?.Cancel();
        _dash?.Dispose();
        _dash = null;
    }
}
