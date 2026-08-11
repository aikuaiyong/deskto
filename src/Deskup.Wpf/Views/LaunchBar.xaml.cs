using Deskup.Core.Config;
using Deskup.Core.Files;
using Deskup.Wpf.Helpers;
using Deskup.Wpf.Services;
using Deskup.Wpf.Shell;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Deskup.Wpf.Views;

/// <summary>快捷启动栏（顶部）：自动隐藏成线条，悬停展开，点击固定。</summary>
public partial class LaunchBar : Window
{
    private readonly AppState _state;
    private bool _pinned;
    private System.Windows.Interop.HwndSourceHook? _bottomHook;

    public LaunchBar(AppState state)
    {
        InitializeComponent();
        _state = state;
        _pinned = _state.Config.LaunchBar.Pinned;

        // 挂桌面层（必须在 Show 之前调用）
        WindowEx.AttachToDesktop(this);

        BtnAdd.Click += (_, _) => AddShortcut();
        BtnPin.Click += (_, _) =>
        {
            _pinned = !_pinned;
            _state.Config.LaunchBar.Pinned = _pinned;
            _state.Save();
            ApplyState();
        };

        MouseEnter += (_, _) => Expand(true);
        MouseLeave += (_, _) => Expand(false);

        Loaded += (_, _) =>
        {
            try
            {
                PositionTop();
                ApplyState();
                Rebuild();
                var hwnd2 = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                WindowEx.SendToBottom(hwnd2);
                _bottomHook = WindowEx.PinToBottom();
                System.Windows.Interop.HwndSource.FromHwnd(hwnd2)?.AddHook(_bottomHook);
            }
            catch (Exception ex) { MessageBox.Show("启动栏加载失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
    }

    private void PositionTop()
    {
        var w = SystemParameters.PrimaryScreenWidth;
        Width = System.Math.Min(760, w - 40);
        Left = (w - Width) / 2;
        Top = 0;
    }

    private void ApplyState()
    {
        Height = _state.Config.LaunchBar.AutoHide && !_pinned ? 6 : 74;
        BtnPin.Content = _pinned ? "📌" : "🔘";
        // 窄条隐藏时不偏移，展开时固定在顶部
    }

    private void Expand(bool on)
    {
        if (_pinned || !_state.Config.LaunchBar.AutoHide) return;
        Height = on ? 74 : 6;
    }

    private void AddShortcut()
    {
        if (_state.Config.LaunchBar.Items.Count >= 10)
        {
            MessageBox.Show("最多添加 10 个快捷方式。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new OpenFileDialog
        {
            Title = "选择快捷方式",
            Filter = "程序或文件|*.exe;*.lnk;*.bat;*.*",
        };
        if (dlg.ShowDialog() != true) return;
        _state.Config.LaunchBar.Items.Add(new LaunchItem
        {
            Path = dlg.FileName,
            Name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName),
        });
        _state.Save();
        Rebuild();
    }

    private void Rebuild()
    {
        Items.Children.Clear();
        foreach (var it in _state.Config.LaunchBar.Items)
        {
            var b = new Button
            {
                Width = 62, Height = 62,
                Margin = new Thickness(2),
                Padding = new Thickness(4),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = it.Path,
                Cursor = Cursors.Hand,
            };
            var img = new Image
            {
                Source = IconHelper.GetFileIcon(it.Path, 48),
                Width = 48, Height = 48,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            b.Content = img;

            b.Click += (_, _) => FileOperations.Open(it.Path);
            var cm = new ContextMenu();
            var miDel = new MenuItem { Header = "🗑 删除" };
            miDel.Click += (_, _) =>
            {
                _state.Config.LaunchBar.Items.Remove(it);
                _state.Save();
                Rebuild();
            };
            cm.Items.Add(miDel);
            b.ContextMenu = cm;

            Items.Children.Add(b);
        }
    }
}
