using Deskup.Core.Config;
using Deskup.Core.Files;

namespace Deskup.Core.Tests;

internal static class FileTests
{
    public static void Register()
    {
        TestHarness.Register("复制模式不删除原桌面文件", CopyKeepsOriginal);
        TestHarness.Register("移动模式会移除原文件", MoveRemovesOriginal);
        TestHarness.Register("跳过不存在的源文件", SkipsMissingSource);
        TestHarness.Register("同名文件自动避免覆盖", AvoidsOverwrite);
        TestHarness.Register("ResolveSaveLocation 建目录并返回路径", ResolveCreatesDir);
    }

    private static void CopyKeepsOriginal()
    {
        var dir = TempDir();
        var src = Path.Combine(dir, "desktop.txt");
        var destDir = Path.Combine(dir, "partition");
        File.WriteAllText(src, "hello");

        var results = FileOperations.PlaceFiles(new[] { src }, destDir, move: false);

        TestHarness.Equal(1, results.Count);
        TestHarness.True(results[0].Success);
        TestHarness.True(File.Exists(src), "原文件应仍存在");
        TestHarness.True(File.Exists(Path.Combine(destDir, "desktop.txt")), "副本应存在");
        TestHarness.Equal("hello", File.ReadAllText(results[0].Dest!));
    }

    private static void MoveRemovesOriginal()
    {
        var dir = TempDir();
        var src = Path.Combine(dir, "moveme.txt");
        var destDir = Path.Combine(dir, "partition");
        File.WriteAllText(src, "data");

        var results = FileOperations.PlaceFiles(new[] { src }, destDir, move: true);

        TestHarness.True(results[0].Success);
        TestHarness.False(File.Exists(src), "原文件应已被移走");
        TestHarness.True(File.Exists(Path.Combine(destDir, "moveme.txt")));
    }

    private static void SkipsMissingSource()
    {
        var dir = TempDir();
        var destDir = Path.Combine(dir, "partition");
        var missing = Path.Combine(dir, "nope.txt");

        var results = FileOperations.PlaceFiles(new[] { missing }, destDir, move: false);

        TestHarness.Equal(1, results.Count);
        TestHarness.False(results[0].Success);
        TestHarness.NotNull(results[0].Error);
    }

    private static void AvoidsOverwrite()
    {
        var dir = TempDir();
        var destDir = Path.Combine(dir, "partition");
        Directory.CreateDirectory(destDir);
        File.WriteAllText(Path.Combine(destDir, "a.txt"), "original");
        var src = Path.Combine(dir, "a.txt");
        File.WriteAllText(src, "new");

        var results = FileOperations.PlaceFiles(new[] { src }, destDir, move: false);

        TestHarness.True(results[0].Success);
        TestHarness.True(results[0].Dest!.EndsWith("a (1).txt"), $"应重命名，实际 {results[0].Dest}");
        TestHarness.Equal("original", File.ReadAllText(Path.Combine(destDir, "a.txt")));
        TestHarness.Equal("new", File.ReadAllText(results[0].Dest!));
    }

    private static void ResolveCreatesDir()
    {
        var dir = TempDir();
        var settings = new AppSettings { DefaultSaveRoot = Path.Combine(dir, "root") };
        var partition = new PartitionConfig { Title = "工作分区" };

        var loc = FileOperations.ResolveSaveLocation(partition, settings);

        TestHarness.True(Directory.Exists(loc), "保存目录应被创建");
        TestHarness.True(loc.Replace('\\', '/').EndsWith("root/工作分区"), $"实际 {loc}");

        // 显式设置且存在则直接用
        var explicitDir = Path.Combine(dir, "explicit");
        Directory.CreateDirectory(explicitDir);
        partition.SaveLocation = explicitDir;
        TestHarness.Equal(explicitDir, FileOperations.ResolveSaveLocation(partition, settings));
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "do_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
