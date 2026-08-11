using Deskup.Core.Geometry;

namespace Deskup.Core.Tests;

internal static class GeometryTests
{
    public static void Register()
    {
        TestHarness.Register("Clamp 限制到屏幕内", ClampInsideScreen);
        TestHarness.Register("Clamp 保证最小尺寸", ClampMinSize);
        TestHarness.Register("IsValid 判定尺寸与坐标", Validity);
        TestHarness.Register("Overlaps 检测重叠", OverlapDetection);
    }

    private static void ClampInsideScreen()
    {
        var r = new Rect(2000, 1500, 300, 200); // 超出 1920x1080
        var c = PartitionBounds.Clamp(r, 1920, 1080);
        TestHarness.True(c.X + c.Width <= 1920, "应在屏幕宽度内");
        TestHarness.True(c.Y + c.Height <= 1080, "应在屏幕高度内");
    }

    private static void ClampMinSize()
    {
        var r = new Rect(50, 50, 10, 5); // 太小
        var c = PartitionBounds.Clamp(r, 1920, 1080);
        TestHarness.True(c.Width >= PartitionBounds.MinWidth);
        TestHarness.True(c.Height >= PartitionBounds.MinHeight);
        TestHarness.True(c.X >= 0 && c.Y >= 0);
    }

    private static void Validity()
    {
        TestHarness.True(PartitionBounds.IsValid(new Rect(0, 0, 200, 150)));
        TestHarness.False(PartitionBounds.IsValid(new Rect(0, 0, 50, 150)), "宽度过小应无效");
        TestHarness.False(PartitionBounds.IsValid(new Rect(-10, 0, 200, 150)), "负坐标应无效");
    }

    private static void OverlapDetection()
    {
        var a = new Rect(0, 0, 200, 200);
        var b = new Rect(100, 100, 200, 200);
        var c = new Rect(500, 500, 100, 100);
        TestHarness.True(PartitionBounds.Overlaps(a, b), "应重叠");
        TestHarness.False(PartitionBounds.Overlaps(a, c), "不应重叠");
    }
}
