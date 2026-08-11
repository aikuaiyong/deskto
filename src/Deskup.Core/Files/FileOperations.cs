using System.Diagnostics;
using Deskup.Core.Config;

namespace Deskup.Core.Files;

/// <summary>单个文件放入分区的结果。</summary>
public sealed record FileResult(string Source, bool Success, string? Error = null, string? Dest = null);

/// <summary>文件操作：解析保存位置、复制/移动、用系统打开。</summary>
public static class FileOperations
{
    /// <summary>
    /// 解析分区的实际保存目录：显式设置且存在则直接用；否则在默认根下按标题建目录。
    /// </summary>
    public static string ResolveSaveLocation(PartitionConfig partition, AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(partition.SaveLocation) && Directory.Exists(partition.SaveLocation))
            return partition.SaveLocation;

        var root = string.IsNullOrWhiteSpace(settings.DefaultSaveRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Deskup")
            : settings.DefaultSaveRoot;

        var dir = Path.Combine(root, Sanitize(partition.Title));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 把一批源文件放入目标目录。
    /// move=false（默认）：复制，<b>不移动原桌面文件</b>；
    /// move=true：移动。
    /// </summary>
    public static IReadOnlyList<FileResult> PlaceFiles(IEnumerable<string> sourcePaths, string destDir, bool move)
    {
        Directory.CreateDirectory(destDir);
        var results = new List<FileResult>();

        foreach (var src in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(src))
            {
                results.Add(new FileResult(src ?? "", false, "路径为空"));
                continue;
            }
            if (!File.Exists(src))
            {
                results.Add(new FileResult(src, false, "源文件不存在"));
                continue;
            }

            var name = Path.GetFileName(src);
            var dest = UniqueName(destDir, name);
            try
            {
                if (move)
                    File.Move(src, dest);
                else
                    File.Copy(src, dest); // 默认复制：原文件保留
                results.Add(new FileResult(src, true, null, dest));
            }
            catch (Exception ex)
            {
                results.Add(new FileResult(src, false, ex.Message));
            }
        }

        return results;
    }

    /// <summary>用系统默认程序打开文件/目录（Windows 下 UseShellExecute=true）。</summary>
    public static bool Open(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return false;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>目标目录内若已存在同名则追加 (1)/(2) 等，避免覆盖。</summary>
    private static string UniqueName(string dir, string name)
    {
        var candidate = Path.Combine(dir, name);
        if (!File.Exists(candidate))
            return candidate;

        var baseName = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var i = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(dir, $"{baseName} ({i}){ext}");
            i++;
        }
        return candidate;
    }

    /// <summary>清洗分区标题，作为目录名（去掉非法字符）。</summary>
    private static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((s ?? "分区").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "分区" : cleaned;
    }
}
