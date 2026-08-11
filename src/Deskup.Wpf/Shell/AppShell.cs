using Deskup.Core.Config;
using Deskup.Wpf.Services;
using Deskup.Wpf.Views;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace Deskup.Wpf.Shell;

/// <summary>管理所有面板窗口、工具栏、启动栏的生命周期与全局动作。</summary>
public sealed class AppShell
{
    private readonly AppState _state;
    private readonly List<PartitionWindow> _panels = new();
    private SidebarWindow? _sidebar;
    private LaunchBar? _launchBar;

    public AppShell(AppState state) => _state = state;

    public void Start()
    {
        // 用户文件分区
        foreach (var p in _state.Config.Partitions)
        {
            try { OpenFiles(p); }
            catch (Exception ex) { MessageBox.Show("分区创建失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        // 侧边栏
        try
        {
            _sidebar = new SidebarWindow(_state, this);
            _sidebar.Show();
        }
        catch (Exception ex) { MessageBox.Show("侧边栏创建失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Warning); }

        // 启动栏
        try
        {
            _launchBar = new LaunchBar(_state);
            _launchBar.Show();
        }
        catch (Exception ex) { MessageBox.Show("启动栏创建失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Warning); }

        ApplyLayer();
        ApplyDesktopIcons();
    }

    private void OpenFixed(PanelKind kind, PartitionConfig visual)
    {
        var w = new PartitionWindow(new PanelModel(kind, visual), _state, this);
        w.Show();
        _panels.Add(w);
    }

    public PartitionWindow OpenFiles(PartitionConfig cfg)
    {
        var w = new PartitionWindow(new PanelModel(PanelKind.Files, cfg, cfg), _state, this);
        w.Show();
        _panels.Add(w);
        return w;
    }

    public void AddPartition()
    {
        var cfg = new PartitionConfig
        {
            Title = "新建分区",
            X = 700,
            Y = 400,
            Width = 260,
            Height = 220,
        };
        _state.Config.Partitions.Add(cfg);
        _state.Save();
        OpenFiles(cfg);
    }

    public void RemovePartition(PartitionWindow w)
    {
        if (w.Model.FileConfig != null)
            _state.Config.Partitions.Remove(w.Model.FileConfig);
        _panels.Remove(w);
        w.Close();
        _state.Save();
    }

    public void ToggleHideAll()
    {
        var anyVisible = _panels.Exists(w => w.Visibility == Visibility.Visible);
        foreach (var w in _panels)
            w.Visibility = anyVisible ? Visibility.Collapsed : Visibility.Visible;
    }

    public void OpenSettings()
    {
        try
        {
            var w = new SettingsWindow(_state, this);
            w.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开设置窗口：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>将所有窗口挂到桌面 WorkerW 层（图标之下），失败则保持普通窗口。</summary>
    public void ApplyLayer()
    {
        foreach (var w in _panels)
            w.ApplyLayer();

        if (_sidebar != null)
            WindowEx.ApplyDesktopLayer(new WindowInteropHelper(_sidebar).Handle);
        if (_launchBar != null)
            WindowEx.ApplyDesktopLayer(new WindowInteropHelper(_launchBar).Handle);
    }

    public void Exit() => Application.Current.Shutdown();

    // 桌面图标显隐——Win10/11 可靠方案：找到 SysListView32/FolderView 直接 ShowWindow
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    /// <summary>找到桌面上 SysListView32 / FolderView 子窗口句柄。</summary>
    private static IntPtr FindDesktopListView()
    {
        IntPtr result = IntPtr.Zero;
        EnumChildWindows(GetDesktopWindow(), (hwnd, _) =>
        {
            var cls = new StringBuilder(256);
            GetClassName(hwnd, cls, 256);
            if (cls.ToString() != "SysListView32") return true;

            var txt = new StringBuilder(256);
            GetWindowText(hwnd, txt, 256);
            if (txt.ToString() == "FolderView")
            {
                result = hwnd;
                return false; // 找到，停止枚举
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private void ApplyDesktopIcons()
    {
        try
        {
            var hide = _state.Config.Settings.HideDesktopIcons;

            // 持久化到注册表
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
            key?.SetValue("HideIcons", hide ? 1 : 0, RegistryValueKind.DWord);

            // 直接操作 SysListView32 窗口
            var hwnd = FindDesktopListView();
            if (hwnd != IntPtr.Zero)
            {
                bool currentlyVisible = IsWindowVisible(hwnd);
                if (hide && currentlyVisible)
                    ShowWindow(hwnd, SW_HIDE);
                else if (!hide && !currentlyVisible)
                    ShowWindow(hwnd, SW_SHOW);
            }
        }
        catch { /* 静默忽略 */ }
    }
}
