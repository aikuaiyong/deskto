namespace Deskto.Core.Geometry;

/// <summary>简单矩形（屏幕坐标，单位与 WPF 一致：与设备无关的像素）。</summary>
public readonly struct Rect
{
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }

    public Rect(double x, double y, double width, double height)
    {
        X = x; Y = y; Width = width; Height = height;
    }

    public bool Contains(double x, double y) =>
        x >= X && x <= X + Width && y >= Y && y <= Y + Height;

    public bool Intersects(Rect o) =>
        X < o.X + o.Width && X + Width > o.X &&
        Y < o.Y + o.Height && Y + Height > o.Y;
}

/// <summary>分区几何约束：最小尺寸、限制在屏幕内、有效性校验。</summary>
public static class PartitionBounds
{
    public const double MinWidth = 120;
    public const double MinHeight = 80;

    /// <summary>把矩形夹到屏幕范围内，并保证不小于最小尺寸。</summary>
    public static Rect Clamp(Rect r, double screenW, double screenH)
    {
        var w = Math.Max(MinWidth, Math.Min(r.Width, screenW));
        var h = Math.Max(MinHeight, Math.Min(r.Height, screenH));
        var x = Math.Max(0, Math.Min(r.X, screenW - w));
        var y = Math.Max(0, Math.Min(r.Y, screenH - h));
        return new Rect(x, y, w, h);
    }

    /// <summary>是否有效：尺寸达标且位于屏幕第一象限内。</summary>
    public static bool IsValid(Rect r) =>
        r.Width >= MinWidth && r.Height >= MinHeight && r.X >= 0 && r.Y >= 0;

    /// <summary>两个分区是否重叠（用于避免完全遮挡）。</summary>
    public static bool Overlaps(Rect a, Rect b) => a.Intersects(b);
}
