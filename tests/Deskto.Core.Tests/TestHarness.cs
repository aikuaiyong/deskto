using System.Globalization;

namespace Deskto.Core.Tests;

/// <summary>
/// 零依赖的最小测试运行器（离线环境无法还原 xunit，故自研）。
/// 每个测试用例是返回 void 的方法，断言失败则抛异常。
/// </summary>
internal static class TestHarness
{
    private static readonly List<(string Name, Action Test)> Tests = new();

    public static void Register(string name, Action test) => Tests.Add((name, test));

    public static int RunAll()
    {
        var pass = 0;
        var fail = 0;
        Console.WriteLine($"运行 {Tests.Count} 个测试...\n");

        foreach (var (name, test) in Tests)
        {
            try
            {
                test();
                pass++;
                Console.WriteLine($"  ✓ {name}");
            }
            catch (Exception ex)
            {
                fail++;
                Console.WriteLine($"  ✗ {name}");
                Console.WriteLine($"      {ex.Message}");
            }
        }

        Console.WriteLine($"\n结果：{pass} 通过, {fail} 失败");
        return fail == 0 ? 0 : 1;
    }

    // ---- 断言 ----
    public static void Equal<T>(T expected, T actual, string msg = "")
    {
        if (!Equals(expected, actual))
            throw new Exception($"{msg}  期望 {Format(expected)} 实际 {Format(actual)}");
    }

    public static void True(bool cond, string msg = "应为 true") =>
        False(!cond, msg);

    public static void False(bool cond, string msg = "应为 false")
    {
        if (cond) throw new Exception(msg);
    }

    public static void Null(object? o, string msg = "应为 null") =>
        False(o is not null, msg);

    public static void NotNull(object? o, string msg = "不应为 null") =>
        False(o is null, msg);

    private static string Format<T>(T v) =>
        v is null ? "null" : Convert.ToString(v, CultureInfo.InvariantCulture) ?? "null";
}
