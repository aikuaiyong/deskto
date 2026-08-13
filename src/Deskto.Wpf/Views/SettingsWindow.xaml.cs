using Deskto.Core.Config;
using Deskto.Wpf.Services;
using Deskto.Wpf.Shell;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace Deskto.Wpf.Views;

/// <summary>设置窗：图层模式、开机自启、桌面图标显隐。</summary>
public partial class SettingsWindow : Window
{
    private readonly AppState _state;
    private readonly AppShell _shell;

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

    public SettingsWindow(AppState state, AppShell shell)
    {
        InitializeComponent();
        _state = state;
        _shell = shell;

        try
        {
            var s = _state.Config.Settings;
            ChkStart.IsChecked = s.StartWithWindows;
            ChkHideIcons.IsChecked = s.HideDesktopIcons;
            // 勾选立即生效
            ChkHideIcons.Checked += (_, _) => ApplyDesktopIcons(true);
            ChkHideIcons.Unchecked += (_, _) => ApplyDesktopIcons(false);
            CmbLayer.SelectedIndex = s.Layer == LayerMode.BehindIcons ? 1 : 0;

            BtnOK.Click += (_, _) => Ok();
            BtnCancel.Click += (_, _) => Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"设置窗口初始化失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Ok()
    {
        var s = _state.Config.Settings;
        s.StartWithWindows = ChkStart.IsChecked == true;
        s.HideDesktopIcons = ChkHideIcons.IsChecked == true;
        // 透明度保留旧值不变（UI 已移除，数据层不动以免破坏已有配置）
        s.Layer = (CmbLayer.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() == "BehindIcons"
            ? LayerMode.BehindIcons
            : LayerMode.Overlay;

        _state.Save();
        SetAutoStart(s.StartWithWindows);
        ApplyDesktopIcons(s.HideDesktopIcons);
        _shell.ApplyLayer();
        Close();
    }

    /// <summary>
    /// Win10/11 可靠方案：
    /// 1. 注册表持久化 HideIcons
    /// 2. 枚举桌面子窗口找到 SysListView32/FolderView
    /// 3. ShowWindow 直接隐藏/显示
    /// </summary>
    private static void ApplyDesktopIcons(bool hide)
    {
        try
        {
            // 持久化到注册表
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
            key?.SetValue("HideIcons", hide ? 1 : 0, RegistryValueKind.DWord);

            // 查找 SysListView32 / FolderView
            IntPtr hwnd = IntPtr.Zero;
            EnumChildWindows(GetDesktopWindow(), (h, _) =>
            {
                var cls = new StringBuilder(256);
                GetClassName(h, cls, 256);
                if (cls.ToString() != "SysListView32") return true;

                var txt = new StringBuilder(256);
                GetWindowText(h, txt, 256);
                if (txt.ToString() == "FolderView")
                {
                    hwnd = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            if (hwnd == IntPtr.Zero) return;

            bool currentlyVisible = IsWindowVisible(hwnd);
            if (hide && currentlyVisible)
                ShowWindow(hwnd, SW_HIDE);
            else if (!hide && !currentlyVisible)
                ShowWindow(hwnd, SW_SHOW);
        }
        catch { /* 静默忽略 */ }
    }

    private static void SetAutoStart(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (on)
                key.SetValue("deskto", Process.GetCurrentProcess().MainModule?.FileName ?? "");
            else
                key.DeleteValue("deskto", false);
        }
        catch
        {
            // 无权限等情况下静默忽略
        }
    }
}
