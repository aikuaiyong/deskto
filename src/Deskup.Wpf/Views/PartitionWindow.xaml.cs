using Deskup.Core.Config;
using Deskup.Core.Files;
using Deskup.Core.Geometry;
using Deskup.Wpf.Helpers;
using Deskup.Wpf.Services;
using Deskup.Wpf.Shell;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// 避免与 System.Windows.Media.Geometry 命名冲突
using CoreRect = Deskup.Core.Geometry.Rect;

namespace Deskup.Wpf.Views;

public partial class PartitionWindow : Window
{
    private readonly PanelModel _model;
    private readonly AppState _state;
    private readonly AppShell _shell;

    private WrapPanel? _filesPanel;
    private StackPanel? _todoPanel;
    private WrapPanel? _labelsPanel;
    private System.Windows.Interop.HwndSourceHook? _bottomHook;

    public PartitionWindow(PanelModel model, AppState state, AppShell shell)
    {
        InitializeComponent();
        _model = model;
        _state = state;
        _shell = shell;
        // 挂桌面层 + 防 Win+D
        WindowEx.AttachToDesktop(this);
        Loaded += OnLoaded;
        BuildContent();
    }

    public PanelModel Model => _model;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var v = _model.Visual;
        Left = v.X;
        Top = v.Y;
        Width = v.Width;
        Height = v.Height;
        // Emoji 渲染需等到 Dispatcher Loaded 才有效
        Dispatcher.BeginInvoke(new Action(() => { ApplyVisual(); UpdateTitleBarHeight(); }),
            System.Windows.Threading.DispatcherPriority.Loaded);
        // WM_WINDOWPOSCHANGING 拦截——永远留在 Z 序底层
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        WindowEx.SendToBottom(hwnd);
        _bottomHook = WindowEx.PinToBottom();
        System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.AddHook(_bottomHook);
    }

    // 保留兼容（AppShell 调用，实际逻辑已移入 OnLoaded）
    public void ApplyLayer() { }

    private void ApplyVisual()
    {
        var v = _model.Visual;
        TitleText.Text = v.Title;
        TitleText.FontSize = v.TitleFontSize > 0 ? v.TitleFontSize : 13;
        // 标题栏：Alpha 通道实现透明度，不影响子元素
        var tBg = ParseColor(v.TitleBgColor ?? "#3A6EA5");
        tBg.A = (byte)(Clamp01(v.TitleOpacity) * 255);
        TitleBar.Background = new SolidColorBrush(tBg);
        // 内容区：Alpha 通道替代 Opacity，图标不再雾化
        var cBg = ParseColor(v.ContentBgColor ?? "#FFFFFF");
        cBg.A = (byte)(Clamp01(v.ContentOpacity) * 255);
        ContentArea.Background = new SolidColorBrush(cBg);
        ResizeGrip.Visibility = v.Locked ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateTitleBarHeight()
    {
        var fs = _model.Visual.TitleFontSize > 0 ? _model.Visual.TitleFontSize : 13;
        TitleBar.Height = fs + 18;
    }

    // ---------- 标题区交互 ----------
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (_model.Visual.Locked) return;
        if (TitleEdit.Visibility == Visibility.Visible) return;
        DragMove();
    }

    private void TitleBar_RightClick(object sender, MouseButtonEventArgs e)
    {
        var cm = new ContextMenu();
        // 锁定/解锁
        var miLock = new MenuItem { Header = _model.Visual.Locked ? "🔓 解锁" : "🔒 锁定" };
        miLock.Click += (_, _) => ToggleLock();
        cm.Items.Add(miLock);
        // 分区配色
        var miColor = new MenuItem { Header = "🎨 分区配色与透明度" };
        miColor.Click += (_, _) => OpenColorSettings();
        cm.Items.Add(miColor);
        // 分隔线 + 删除（仅文件分区）
        if (_model.Kind == PanelKind.Files)
        {
            cm.Items.Add(new Separator());
            var miDel = new MenuItem { Header = "✕ 删除分区" };
            miDel.Click += (_, _) => _shell.RemovePartition(this);
            cm.Items.Add(miDel);
        }
        TitleBar.ContextMenu = cm;
        cm.IsOpen = true;
        e.Handled = true;
    }

    private void ToggleLock()
    {
        _model.Visual.Locked = !_model.Visual.Locked;
        ResizeGrip.Visibility = _model.Visual.Locked ? Visibility.Collapsed : Visibility.Visible;
        _state.Save();
    }

    private void OpenColorSettings()
    {
        var popup = new Window
        {
            Title = $"分区设置 - {_model.Visual.Title}",
            Width = 320, Height = 600,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };
        var panel = new StackPanel { Margin = new Thickness(12) };
        var colorBox = new TextBox { Text = _model.Visual.TitleBgColor ?? "#3A6EA5", Margin = new Thickness(0, 0, 0, 6) };

        panel.Children.Add(new TextBlock { Text = "标题栏颜色", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 6) });
        var swatchGrid = new WrapPanel();
        var paletteColors = new[] { "#E74C3C","#E67E22","#F1C40F","#2ECC71","#1ABC9C","#3498DB","#9B59B6","#34495E","#E91E63","#FF5722","#FFC107","#4CAF50","#00BCD4","#2196F3","#673AB7","#607D8B","#F44336","#FF9800","#8BC34A","#3F51B5" };
        foreach (var c in paletteColors)
        {
            var sw = new Border { Width = 32, Height = 32, Margin = new Thickness(2), CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(ParseColor(c)), Cursor = Cursors.Hand, Tag = c };
            sw.MouseLeftButtonDown += (s, _) => {
                _model.Visual.TitleBgColor = (string)((Border)s!).Tag;
                colorBox.Text = (string)((Border)s!).Tag;
                _state.Save();
                ApplyVisual();
            };
            swatchGrid.Children.Add(sw);
        }
        panel.Children.Add(swatchGrid);

        panel.Children.Add(new TextBlock { Text = "或输入色号 (#RRGGBB)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 4) });
        panel.Children.Add(colorBox);

        panel.Children.Add(new TextBlock { Text = $"标题栏透明度 ({_model.Visual.TitleOpacity * 100:F0}%)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 2) });
        var titleOpacitySlider = new Slider { Minimum = 0.1, Maximum = 1.0, Value = _model.Visual.TitleOpacity, TickFrequency = 0.1, IsSnapToTickEnabled = true };
        var titleOpacityLabel = (TextBlock)panel.Children[^1];
        titleOpacitySlider.ValueChanged += (_, _) => {
            titleOpacityLabel.Text = $"标题栏透明度 ({titleOpacitySlider.Value * 100:F0}%)";
            var bg = ParseColor(_model.Visual.TitleBgColor ?? "#3A6EA5");
            bg.A = (byte)(Clamp01(titleOpacitySlider.Value) * 255);
            TitleBar.Background = new SolidColorBrush(bg);
        };
        panel.Children.Add(titleOpacitySlider);

        // 标题字号
        panel.Children.Add(new TextBlock { Text = $"标题字号 ({_model.Visual.TitleFontSize})", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 4) });
        var titleFontLabel = (TextBlock)panel.Children[^1];
        var titleFontSlider = new Slider { Minimum = 10, Maximum = 28, Value = _model.Visual.TitleFontSize, TickFrequency = 1, IsSnapToTickEnabled = true };
        titleFontSlider.ValueChanged += (_, _) => {
            var fs = (int)titleFontSlider.Value;
            titleFontLabel.Text = $"标题字号 ({fs})";
            TitleText.FontSize = fs;
            TitleEdit.FontSize = fs;
            // 标题栏高度自适应
            TitleBar.Height = fs + 18;
        };
        panel.Children.Add(titleFontSlider);

        panel.Children.Add(new TextBlock { Text = $"内容区透明度 ({_model.Visual.ContentOpacity * 100:F0}%)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 2) });
        var contentOpacitySlider = new Slider { Minimum = 0.1, Maximum = 1.0, Value = _model.Visual.ContentOpacity, TickFrequency = 0.1, IsSnapToTickEnabled = true };
        var contentOpacityLabel = (TextBlock)panel.Children[^1];
        contentOpacitySlider.ValueChanged += (_, _) => {
            contentOpacityLabel.Text = $"内容区透明度 ({contentOpacitySlider.Value * 100:F0}%)";
            var bg = ParseColor(_model.Visual.ContentBgColor ?? "#FFFFFF");
            bg.A = (byte)(Clamp01(contentOpacitySlider.Value) * 255);
            ContentArea.Background = new SolidColorBrush(bg);
        };
        panel.Children.Add(contentOpacitySlider);

        // 内容区背景色
        var contentColorBox = new TextBox { Text = _model.Visual.ContentBgColor ?? "#FFFFFF", Margin = new Thickness(0, 0, 0, 6) };
        panel.Children.Add(new TextBlock { Text = "内容区背景色", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 6) });
        var contentSwatchGrid = new WrapPanel();
        foreach (var c in paletteColors)
        {
            var sw = new Border { Width = 32, Height = 32, Margin = new Thickness(2), CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(ParseColor(c)), Cursor = Cursors.Hand, Tag = c };
            sw.MouseLeftButtonDown += (s, _) => {
                _model.Visual.ContentBgColor = (string)((Border)s!).Tag;
                contentColorBox.Text = (string)((Border)s!).Tag;
                _state.Save();
                var bg = ParseColor(_model.Visual.ContentBgColor ?? "#FFFFFF");
                bg.A = (byte)(Clamp01(contentOpacitySlider.Value) * 255);
                ContentArea.Background = new SolidColorBrush(bg);
            };
            contentSwatchGrid.Children.Add(sw);
        }
        panel.Children.Add(contentSwatchGrid);
        panel.Children.Add(new TextBlock { Text = "或输入色号 (#RRGGBB)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 4) });
        panel.Children.Add(contentColorBox);

        var btn = new Button { Content = "关闭", Height = 32, Margin = new Thickness(0, 12, 0, 0), Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x6E, 0xA5)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        btn.Click += (_, _) =>
        {
            _model.Visual.TitleOpacity = Clamp01(titleOpacitySlider.Value);
            _model.Visual.ContentOpacity = Clamp01(contentOpacitySlider.Value);
            _model.Visual.TitleFontSize = (int)titleFontSlider.Value;
            _state.Save();
            popup.Close();
        };
        panel.Children.Add(btn);

        popup.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        popup.ShowDialog();
    }

    private void TitleText_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && !_model.Visual.Locked)
        {
            TitleEdit.Text = _model.Visual.Title;
            TitleText.Visibility = Visibility.Collapsed;
            TitleEdit.Visibility = Visibility.Visible;
            TitleEdit.Focus();
            TitleEdit.SelectAll();
            e.Handled = true;
        }
    }

    private void TitleEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Escape) CommitTitle();
    }

    private void TitleEdit_Lost(object sender, RoutedEventArgs e) => CommitTitle();

    private void CommitTitle()
    {
        if (TitleEdit.Visibility != Visibility.Visible) return;
        var t = TitleEdit.Text.Trim();
        if (!string.IsNullOrEmpty(t)) _model.Visual.Title = t;
        TitleEdit.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        TitleText.Text = _model.Visual.Title;
        if (_model.Kind == PanelKind.Files) _state.Save();
    }

    // ---------- 拖拽移动 / 缩放 ----------
    private bool _resizing;
    private Point _resizeStart;

    private void ResizeGrip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_model.Visual.Locked) return;
        if (e.ChangedButton != MouseButton.Left) return;
        _resizing = true;
        _resizeStart = e.GetPosition(this);
        ResizeGrip.CaptureMouse();
        e.Handled = true;
    }

    private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizing) return;
        var pos = e.GetPosition(this);
        var dx = pos.X - _resizeStart.X;
        var dy = pos.Y - _resizeStart.Y;
        _resizeStart = pos;

        var newW = Width + dx;
        var newH = Height + dy;
        var clamped = PartitionBounds.Clamp(
            new CoreRect(Left, Top, newW, newH),
            double.MaxValue, double.MaxValue);
        Width = clamped.Width;
        Height = clamped.Height;
    }

    private void ResizeGrip_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        ResizeGrip.ReleaseMouseCapture();
        SaveGeometry();
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        if (IsLoaded)
        {
            ClampToDesktop();
            SaveGeometry();
        }
        base.OnLocationChanged(e);
    }

    /// <summary>限制窗口不能完全拖出桌面，确保标题栏至少 30px 可见（避免锁按钮跑到屏幕外无法解锁）。</summary>
    private void ClampToDesktop()
    {
        var wa = SystemParameters.WorkArea;
        if (Left < wa.Left - Width + 30) Left = wa.Left - Width + 30;
        if (Left > wa.Right - 30) Left = wa.Right - 30;
        if (Top < wa.Top) Top = wa.Top;
        if (Top > wa.Bottom - 30) Top = wa.Bottom - 30;
    }

    private void SaveGeometry()
    {
        var v = _model.Visual;
        v.X = Left;
        v.Y = Top;
        v.Width = Width;
        v.Height = Height;
        _state.Save();
    }

    // ---------- 文件拖放 ----------
    private void ContentArea_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ContentArea_Drop(object sender, DragEventArgs e)
    {
        if (_model.Kind != PanelKind.Files) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (paths == null) return;
        PlaceIntoPartition(paths);
    }

    private void PlaceIntoPartition(string[] paths)
    {
        var dest = FileOperations.ResolveSaveLocation(_model.Visual, _state.Config.Settings);
        var move = _state.Config.Settings.MoveInsteadOfCopy; // 默认复制：不移动原桌面文件
        var results = FileOperations.PlaceFiles(paths, dest, move);
        foreach (var r in results.Where(r => r.Success && r.Dest != null))
        {
            var d = r.Dest!;
            _model.Visual.Items.Add(new PartitionItem { Path = d, DisplayName = Path.GetFileName(d) });
        }
        _state.Save();
        RebuildFiles();
    }

    private void AddFile()
    {
        var dlg = new OpenFileDialog { Multiselect = true, Title = "选择要放入分区的文件" };
        if (dlg.ShowDialog() == true)
            PlaceIntoPartition(dlg.FileNames);
    }

    // ---------- 内容构建（按类型切换） ----------
    private void BuildContent()
    {
        ContentHost.Content = _model.Kind switch
        {
            PanelKind.Files => BuildFiles(),
            PanelKind.Schedule => BuildSchedule(),
            PanelKind.Todo => BuildTodo(),
            PanelKind.Labels => BuildLabels(),
            _ => null,
        };
    }

    private UIElement BuildFiles()
    {
        _filesPanel = new WrapPanel { Orientation = Orientation.Horizontal };
        var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _filesPanel };
        RebuildFiles();
        return sv;
    }

    private void RebuildFiles()
    {
        if (_filesPanel == null) return;
        SyncFiles(); // 与磁盘同步
        _filesPanel.Children.Clear();
        foreach (var it in _model.Visual.Items)
        {
            var displayName = GetDisplayName(it);
            var item = new Border
            {
                Width = 96, Height = 100,
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = it,
            };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var icon = new Image { Source = IconHelper.GetFileIcon(it.Path, 48), Width = 48, Height = 48, Margin = new Thickness(0, 0, 0, 2) };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            var name = new TextBlock { Text = displayName, FontSize = 10, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 88, MaxHeight = 32, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brushes.Black };
            stack.Children.Add(icon);
            stack.Children.Add(name);
            item.Child = stack;

            item.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) FileOperations.Open(it.Path);
            };
            var cm = new ContextMenu();
            var miOpen = new MenuItem { Header = "📂 打开" };
            miOpen.Click += (_, _) => FileOperations.Open(it.Path);
            var miLocate = new MenuItem { Header = "📁 打开文件位置" };
            miLocate.Click += (_, _) => System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + it.Path + "\"");
            var miDel = new MenuItem { Header = "🗑 删除" };
            miDel.Click += (_, _) =>
            {
                DeletePartitionFile(it);
            };
            cm.Items.Add(miOpen);
            cm.Items.Add(miLocate);
            cm.Items.Add(new Separator());
            cm.Items.Add(miDel);
            item.ContextMenu = cm;
            item.MouseEnter += (s, _) => ((Border)s!).Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE8, 0xF0));
            item.MouseLeave += (s, _) => ((Border)s!).Background = Brushes.Transparent;

            _filesPanel.Children.Add(item);
        }
    }

    /// <summary>同步分区项与磁盘文件：新增的自动加入，已删除的移除。</summary>
    private void SyncFiles()
    {
        var saveDir = FileOperations.ResolveSaveLocation(_model.Visual, _state.Config.Settings);
        if (!Directory.Exists(saveDir)) return;

        var diskPaths = new HashSet<string>(
            Directory.GetFiles(saveDir).Select(p => p.Replace('/', '\\')),
            StringComparer.OrdinalIgnoreCase);

        // 移除磁盘已不存在的项
        _model.Visual.Items.RemoveAll(it => !diskPaths.Contains(it.Path.Replace('/', '\\')));

        // 加入磁盘新增的文件
        var knownPaths = new HashSet<string>(
            _model.Visual.Items.Select(it => it.Path.Replace('/', '\\')),
            StringComparer.OrdinalIgnoreCase);

        foreach (var diskPath in diskPaths)
        {
            if (!knownPaths.Contains(diskPath))
                _model.Visual.Items.Add(new PartitionItem
                {
                    Path = diskPath,
                    DisplayName = Path.GetFileName(diskPath),
                });
        }

        if (_model.Visual.Items.Count != knownPaths.Count)
            _state.Save();
    }

    /// <summary>删除分区文件：同时删除磁盘文件和分区记录。</summary>
    private void DeletePartitionFile(PartitionItem it)
    {
        try
        {
            if (File.Exists(it.Path))
                File.Delete(it.Path);
        }
        catch { /* 删除失败不阻塞 UI */ }
        _model.Visual.Items.Remove(it);
        _state.Save();
        RebuildFiles();
    }

    /// <summary>桌面风格显示名：去掉 .lnk 后缀。</summary>
    private static string GetDisplayName(PartitionItem it)
    {
        var name = it.DisplayName;
        if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name;
    }

    private UIElement BuildSchedule()
    {
        var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        var names = new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
        var grid = new Grid();
        for (var i = 0; i < 7; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var i = 0; i < 7; i++)
        {
            var d = days[i];
            var cell = new Grid();
            cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock { Text = names[i], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), FontWeight = FontWeights.Bold };
            var tb = new TextBox { Text = _state.Config.Schedule.Get(d), Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(4), BorderThickness = new Thickness(1) };
            tb.TextChanged += (_, _) => { _state.Config.Schedule.Set(d, tb.Text); _state.Save(); };

            Grid.SetColumn(label, 0);
            Grid.SetColumn(tb, 1);
            cell.Children.Add(label);
            cell.Children.Add(tb);
            Grid.SetRow(cell, i);
            grid.Children.Add(cell);
        }
        return new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement BuildTodo()
    {
        _todoPanel = new StackPanel();
        var add = new Button { Content = "＋ 添加待办事项", Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 2, 6, 2), Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x57, 0x84)) };
        add.Click += (_, _) =>
        {
            _state.Config.Todos.Add(new TodoItem { Text = "新待办" });
            _state.Save();
            RebuildTodo();
        };
        var dock = new DockPanel();
        DockPanel.SetDock(add, Dock.Top);
        dock.Children.Add(add);
        dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _todoPanel });
        RebuildTodo();
        return dock;
    }

    private void RebuildTodo()
    {
        if (_todoPanel == null) return;
        _todoPanel.Children.Clear();
        foreach (var t in _state.Config.Todos)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cb = new CheckBox { IsChecked = t.Done, VerticalAlignment = VerticalAlignment.Center };
            cb.Checked += (_, _) => { t.Done = true; _state.Save(); };
            cb.Unchecked += (_, _) => { t.Done = false; _state.Save(); };

            var tb = new TextBox { Text = t.Text, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            tb.TextChanged += (_, _) => { t.Text = tb.Text; _state.Save(); };

            var del = new Button { Content = "✕", Width = 22, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            del.Click += (_, _) => { _state.Config.Todos.Remove(t); _state.Save(); RebuildTodo(); };

            Grid.SetColumn(cb, 0);
            Grid.SetColumn(tb, 1);
            Grid.SetColumn(del, 2);
            row.Children.Add(cb);
            row.Children.Add(tb);
            row.Children.Add(del);
            _todoPanel.Children.Add(row);
        }
    }

    private UIElement BuildLabels()
    {
        _labelsPanel = new WrapPanel { Orientation = Orientation.Horizontal };
        var add = new Button { Content = "＋ 添加标签", Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(6, 2, 6, 2), Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x57, 0x84)) };
        add.Click += (_, _) =>
        {
            _state.Config.Labels.Add(new ColorLabel { Text = "新标签" });
            _state.Save();
            RebuildLabels();
        };
        var dock = new DockPanel();
        DockPanel.SetDock(add, Dock.Top);
        dock.Children.Add(add);
        dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _labelsPanel });
        RebuildLabels();
        return dock;
    }

    private void RebuildLabels()
    {
        if (_labelsPanel == null) return;
        _labelsPanel.Children.Clear();
        foreach (var l in _state.Config.Labels)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(ParseColor(l.BgColor)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 6, 6),
            };
            var tb = new TextBox
            {
                Text = l.Text,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = l.FontSize,            // 标签高度随字号自适应
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
            };
            tb.TextChanged += (_, _) => { l.Text = tb.Text; _state.Save(); };
            border.Child = tb;
            border.MouseRightButtonDown += (_, _) => { _state.Config.Labels.Remove(l); _state.Save(); RebuildLabels(); };
            _labelsPanel.Children.Add(border);
        }
    }

    // ---------- 工具 ----------
    private static Color ParseColor(string? hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex ?? "#3A6EA5")!;
        }
        catch
        {
            return Color.FromRgb(0x3A, 0x6E, 0xA5);
        }
    }

    private static double Clamp01(double v) => Math.Max(0, Math.Min(1, v));
}
