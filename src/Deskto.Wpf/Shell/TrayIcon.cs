using System.Drawing;
using System.Windows.Forms;
using Deskto.Wpf.Views;

namespace Deskto.Wpf.Shell;

/// <summary>系统托盘图标与右键菜单。</summary>
internal static class TrayIcon
{
    private static NotifyIcon? _icon;

    public static void Create(App app, AppShell shell, bool showStartupTip = false)
    {
        var cm = new ContextMenuStrip();
        cm.Items.Add("显示/隐藏全部", null, (_, _) => shell.ToggleHideAll());
        cm.Items.Add("添加分区", null, (_, _) => shell.AddPartition());
        cm.Items.Add("解锁所有分区", null, (_, _) => shell.UnlockAllPartitions());
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
            Text = "deskto",
            ContextMenuStrip = cm,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => shell.ToggleHideAll();

        // 首次运行时气泡提示，避免用户不知道程序已启动（分区是半透明背景，存在感很低）
        if (showStartupTip)
            _icon.ShowBalloonTip(6000, "deskto 已启动",
                "右键托盘图标可添加分区 / 解锁分区 / 设置 / 退出。", ToolTipIcon.Info);
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
