using System.Diagnostics;

namespace Deskto.Core.Files;

/// <summary>文件操作：用系统默认程序打开文件/目录/程序（供启动栏等使用）。</summary>
public static class FileOperations
{
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
}
