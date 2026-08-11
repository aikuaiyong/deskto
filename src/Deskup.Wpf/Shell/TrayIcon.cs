using System.Drawing;
using System.Windows.Forms;
using Deskup.Wpf.Views;

namespace Deskup.Wpf.Shell;

/// <summary>系统托盘图标与右键菜单。</summary>
internal static class TrayIcon
{
    private static NotifyIcon? _icon;

    public static void Create(App app, AppShell shell)
    {
        var cm = new ContextMenuStrip();
        cm.Items.Add("显示/隐藏全部", null, (_, _) => shell.ToggleHideAll());
        cm.Items.Add("添加分区", null, (_, _) => shell.AddPartition());
        cm.Items.Add("设置", null, (_, _) => shell.OpenSettings());
        cm.Items.Add(new ToolStripSeparator());
        cm.Items.Add("退出", null, (_, _) => app.Shutdown());

        // 从 exe 提取内嵌图标（csproj 中已设 ApplicationIcon）
        // 用 ProcessPath 而非 Assembly.Location，单文件发布兼容
        System.Drawing.Icon appIcon;
        try
        {
            var exePath = Environment.ProcessPath;
            appIcon = !string.IsNullOrEmpty(exePath)
                ? System.Drawing.Icon.ExtractAssociatedIcon(exePath)!
                : SystemIcons.Application;
        }
        catch
        {
            appIcon = SystemIcons.Application;
        }

        _icon = new NotifyIcon
        {
            Icon = appIcon,
            Text = "Deskup",
            ContextMenuStrip = cm,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => shell.ToggleHideAll();
    }

    public static void Dispose()
    {
        if (_icon != null)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
    }
}
