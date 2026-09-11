using Avalonia;
using Avalonia.Media;

namespace NodeEditor.Avalonia.Control;

internal static class WireGeometry
{
    /// <summary>
    /// 按节点编辑器的三次贝塞尔规则生成核心连线。
    /// </summary>
    public static StreamGeometry BuildCore(Point start, Point end)
    {
        var (p0, p1, p2, p3) = Controls(start, end);
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(p0, false);
        context.CubicBezierTo(p1, p2, p3);
        context.EndFigure(false);
        return geometry;
    }

    /// <summary>
    /// 生成绕核心线一圈的闭合轮廓：两侧平行偏移，线头线尾用半圆包住，outlineRadius 是虚线到核心线的间距。
    /// </summary>
    public static StreamGeometry BuildOutline(Point start, Point end, double outlineRadius)
    {
        var samples = Sample(start, end, 72);
        var left = new Point[samples.Length];
        var right = new Point[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            var (point, tangent) = samples[i];
            var normal = new Vector(tangent.Y, -tangent.X);
            left[i] = point + normal * outlineRadius;
            right[i] = point - normal * outlineRadius;
        }

        var radius = new Size(outlineRadius, outlineRadius);
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(left[0], false);
        for (var i = 1; i < left.Length; i++)
            context.LineTo(left[i]);
        context.ArcTo(right[^1], radius, 0, false, SweepDirection.Clockwise);
        for (var i = right.Length - 2; i >= 0; i--)
            context.LineTo(right[i]);
        context.ArcTo(left[0], radius, 0, false, SweepDirection.Clockwise);
        context.EndFigure(true);
        return geometry;
    }

    private static (Point p0, Point p1, Point p2, Point p3) Controls(Point start, Point end)
    {
        var dx = Math.Max(Math.Abs(end.X - start.X) * 0.5, 48);
        return (start, new Point(start.X + dx, start.Y), new Point(end.X - dx, end.Y), end);
    }

    private static (Point Point, Vector Tangent)[] Sample(Point start, Point end, int count)
    {
        var (p0, p1, p2, p3) = Controls(start, end);
        var points = new (Point, Vector)[count];
        Vector last = new(1, 0);
        for (var i = 0; i < count; i++)
        {
            var t = i / (double)(count - 1);
            var mt = 1 - t;
            var point = mt * mt * mt * p0
                        + 3 * mt * mt * t * p1
                        + 3 * mt * t * t * p2
                        + t * t * t * p3;
            var tangent =
                3 * mt * mt * (p1 - p0) +
                6 * mt * t * (p2 - p1) +
                3 * t * t * (p3 - p2);
            var vector = new Vector(tangent.X, tangent.Y);
            if (vector.SquaredLength < 0.0001)
                vector = last;
            else
            {
                vector = vector.Normalize();
                last = vector;
            }

            points[i] = (point, vector);
        }

        return points;
    }
}
