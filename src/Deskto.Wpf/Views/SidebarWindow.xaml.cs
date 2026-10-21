using Deskto.Core.Config;
using Deskto.Wpf.Services;
using Deskto.Wpf.Shell;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Deskto.Wpf.Views;

/// <summary>
/// 右侧侧边栏：四区合一
/// - 功能设置区（添加分区 / 设置 / 外观 / 解锁 / 退出）
/// - 彩色标签区（增删标签、编辑文字和背景色）
/// - 待办事项区（增删待办、勾选完成）
/// - 临时便签（自由格式文本）
/// 支持折叠/展开、左侧拖拽调整宽度，高度随内容自适应。
/// </summary>
public partial class SidebarWindow : Window
{
    private readonly AppState _state;
    private readonly AppShell _shell;
    private bool _collapsed;
    private bool _onRight = true;
    private double _expandedWidth = 292;
    private bool _resizing;
    private double _resizeStartWidth;
    private double _resizeStartScreenDip;   // 按下瞬间鼠标的屏幕坐标（DIP），用于算拖拽增量
    private bool _resizeUnlocked;  // 解锁后才能拖边缘调宽度
    private bool _overResizeEdge;  // 当前是否正处于可拖拽边缘（用于只在切换时改光标）
    private System.Windows.Interop.HwndSourceHook? _bottomHook;

    private const double MinSidebarWidth = 180;  // 展开态最小宽度，避免被压成一条看不见
    private const double EdgeGrabZone = 8;       // 边缘可抓取宽度
    private const double ResizeDeadZone = 3;     // 拖拽死区，防止单击边缘时抖动

    public SidebarWindow(AppState state, AppShell shell)
    {
        InitializeComponent();
        _state = state;
        _shell = shell;

        // 挂桌面层（必须在 Show 之前调用）
        WindowEx.AttachToDesktop(this);

        // 功能设置按钮
        BtnAdd.Click += (_, _) => _shell.AddPartition();
        BtnUnlock.Click += (_, _) => _shell.UnlockAllPartitions();
        BtnSet.Click += (_, _) => _shell.OpenSettings();
        BtnExit.Click += (_, _) => _shell.Exit();

        // 折叠/展开 + 左右切换 + 外观
        BtnCollapse.Click += (_, _) => ToggleCollapse();
        BtnSideToggle.Click += (_, _) => ToggleSide();
        BtnStyle.Click += (_, _) => EditSidebarStyle();

        // 解锁拖拽调宽度
        BtnResizeLock.Click += (_, _) => ToggleResizeLock();

        // 功能设置面板折叠/展开
        BtnSettingsToggle.Click += (_, _) => ToggleSettingsPanel();

        // 标签
        BtnAddLabel.Click += (_, _) => AddLabel();

        // 待办
        BtnAddTodo.Click += (_, _) => AddTodo();

        // 便签背景色
        BtnStickyBg.Click += (_, _) => EditStickyBg();


        // 挂桌面层（必须在 Loaded 之前注册）
        Loaded += (_, _) =>
        {
            try
            {
                // 应用保存的侧边栏外观（背景色 + 透明度）
                var sb = _state.Config.Sidebar;
                MainBorder.Background = new SolidColorBrush(ParseColor(sb.BgColor)) { Opacity = sb.Opacity };
                // 应用保存的布局（折叠 / 左右 / 宽度）
                _collapsed = sb.Collapsed;
                _onRight = sb.OnRight;
                // 旧版本可能因 DPI 计算的 bug 把宽度存成了 38 这种不可用值，这里自动恢复默认宽度并写回配置
                if (sb.Width < MinSidebarWidth)
                {
                    _expandedWidth = 292;
                    sb.Width = 292;
                    _state.Save();
                }
                else
                {
                    _expandedWidth = sb.Width;
                }
                ApplySidebarLayout();

                RebuildLabels();
                RebuildTodo();
                StickyNote.Text = _state.Config.StickyNote;
                StickyBgBorder.Background = ParseHexBrush(_state.Config.StickyNoteBgColor);
                // WM_WINDOWPOSCHANGING 拦截——永远留在 Z 序底层
                var hwnd2 = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                WindowEx.SendToBottom(hwnd2);
                _bottomHook = WindowEx.PinToBottom();
                System.Windows.Interop.HwndSource.FromHwnd(hwnd2)?.AddHook(_bottomHook);
            }
            catch (Exception ex) { MessageBox.Show("侧边栏加载失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        // 窗口级鼠标事件（处理宽度拖拽）
        AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(OnWindowMouseDown), true);
        MouseMove += OnWindowMouseMove;
        MouseLeftButtonUp += OnWindowMouseUp;
        MouseLeave += OnWindowMouseLeave;
        // 捕获意外丢失（如 Alt+Tab、窗口被抢占、鼠标在窗口外松开）时收尾并保存宽度
        LostMouseCapture += (_, _) =>
        {
            if (_resizing)
            {
                _state.Config.Sidebar.Width = _expandedWidth;
                _state.Save();
                _resizing = false;
            }
            _overResizeEdge = false;
            Cursor = Cursors.Arrow;
        };
    }

    private DateTime _lastStickySave = DateTime.MinValue;

    private void StickyNote_TextChanged(object sender, TextChangedEventArgs e)
    {
        _state.Config.StickyNote = StickyNote.Text;   // 内存始终最新
        AdjustHeight();                               // 内容变高时侧边栏跟着长
        // 每敲一个键都同步写盘会明显卡顿，做 1 秒节流；退出时 App.OnExit 会兜底保存
        var now = DateTime.Now;
        if ((now - _lastStickySave).TotalMilliseconds < 1000) return;
        _lastStickySave = now;
        _state.Save();
    }

    private void EditStickyBg()
    {
        var popup = new Window
        {
            Title = "便签背景色",
            Width = 300, Height = 270,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };
        var panel = new StackPanel { Margin = new Thickness(12) };

        // 预设色板
        panel.Children.Add(new TextBlock { Text = "选择颜色", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 6) });
        var swatchGrid = new WrapPanel();
        var bgColors = new[] {
            "#2D2D3F","#3A3A4A","#1A1A2E","#1E3A5F","#2D4A3A",
            "#4A2D3A","#3A3A2D","#3A2D4A","#2A3A4A","#4A3A2D",
            "#1E1E1E","#3A1E1E","#1E3A1E","#1E1E3A","#3A3A1E",
            "#2E1A4A","#4A1A2E","#1A4A3A","#4A3A1A","#1A2E4A",
        };

        foreach (var c in bgColors)
        {
            var sw = new Border
            {
                Width = 40, Height = 30, Margin = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                Background = ParseHexBrush(c), Cursor = Cursors.Hand, Tag = c,
            };
            sw.MouseLeftButtonDown += (s, _) =>
            {
                var color = (string)((Border)s!).Tag;
                ApplyStickyBg(color);
                popup.Close();
            };
            swatchGrid.Children.Add(sw);
        }
        panel.Children.Add(swatchGrid);

        // 自定义色号
        panel.Children.Add(new TextBlock { Text = "自定义色号 (#RRGGBB)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4) });
        var dock = new DockPanel();
        var hexBox = new TextBox { Text = _state.Config.StickyNoteBgColor ?? "#3A3A4A" };
        hexBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ApplyStickyBg(hexBox.Text.Trim());
                popup.Close();
            }
        };
        var applyBtn = new Button
        {
            Content = "应用", Width = 50, Height = 22, Margin = new Thickness(6, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x6E, 0xA5)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
        };
        applyBtn.Click += (_, _) => { ApplyStickyBg(hexBox.Text.Trim()); popup.Close(); };
        DockPanel.SetDock(applyBtn, Dock.Right);
        dock.Children.Add(applyBtn);
        dock.Children.Add(hexBox);
        panel.Children.Add(dock);

        // 关闭
        var close = new Button
        {
            Content = "关闭", Width = 70, Height = 28, Margin = new Thickness(0, 12, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        close.Click += (_, _) => popup.Close();
        panel.Children.Add(close);

        popup.Content = panel;
        popup.ShowDialog();
    }

    private void ApplyStickyBg(string color)
    {
        var brush = ParseHexBrush(color);
        StickyBgBorder.Background = brush;
        _state.Config.StickyNoteBgColor = color;
        _state.Save();
    }

    // ============ 宽度拖拽（解锁后，鼠标靠近边缘触发） ============

    private void ToggleResizeLock()
    {
        _resizeUnlocked = !_resizeUnlocked;
        BtnResizeLock.Content = _resizeUnlocked ? "🔓" : "🔒";
        BtnResizeLock.ToolTip = _resizeUnlocked ? "已解锁：拖动边缘调整宽度" : "已锁定：点击解锁";
        _overResizeEdge = false;
        Cursor = Cursors.Arrow; // 状态变化后重置，下次移动时重新判定
    }

    private void ToggleSettingsPanel()
    {
        SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
        AdjustHeight();
    }

    /// <summary>鼠标是否落在可拖拽的边缘条上（右侧贴边时在左缘，左侧贴边时在右缘）。</summary>
    private bool IsNearResizeEdge(double x)
    {
        if (_collapsed) return false;
        return _onRight ? (x <= EdgeGrabZone) : (x >= Width - EdgeGrabZone);
    }

    /// <summary>命中测到的元素是否属于按钮——顶部按钮区不能触发边缘拖拽，否则点折叠键会误拖。</summary>
    private static bool IsInsideButton(object? source)
    {
        var d = source as DependencyObject;
        while (d != null)
        {
            if (d is Button) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_resizeUnlocked || _collapsed) return;
        if (IsInsideButton(e.OriginalSource)) return;

        var pos = e.GetPosition(this);
        if (!IsNearResizeEdge(pos.X)) return;

        _resizing = true;
        _resizeStartWidth = Width;
        // 用「Left + 窗口内 DIP 坐标」得到屏幕 DIP 坐标。
        // 关键：绝不能用 PointToScreen（它返回物理像素），否则 125%/150% 缩放下会算出负数宽度。
        _resizeStartScreenDip = Left + pos.X;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (!_resizing)
        {
            // 靠近边缘时显示左右拖拽箭头；只在状态切换时设 Cursor，避免覆盖子控件（如便签输入框）自己的光标
            var canResize = _resizeUnlocked && IsNearResizeEdge(pos.X) && !IsInsideButton(e.OriginalSource);
            if (canResize != _overResizeEdge)
            {
                _overResizeEdge = canResize;
                Cursor = canResize ? Cursors.SizeWE : Cursors.Arrow;
            }
            return;
        }

        var screenDip = Left + pos.X;
        var delta = screenDip - _resizeStartScreenDip;
        if (Math.Abs(delta) < ResizeDeadZone) return;   // 死区：纯点击不改变宽度

        // 右侧贴边：鼠标向左拖变宽；左侧贴边：鼠标向右拖变宽
        double newW = _onRight ? _resizeStartWidth - delta : _resizeStartWidth + delta;
        if (newW < MinSidebarWidth) newW = MinSidebarWidth;
        var maxW = Math.Max(MinSidebarWidth, SystemParameters.PrimaryScreenWidth / 3);
        if (newW > maxW) newW = maxW;

        _expandedWidth = newW;
        ApplySidebarLayout();
    }

    private void OnWindowMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizing)
        {
            _state.Config.Sidebar.Width = _expandedWidth;
            _state.Save();
        }
        _resizing = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        _overResizeEdge = false;
        Cursor = Cursors.Arrow;
    }

    private void OnWindowMouseLeave(object sender, MouseEventArgs e)
    {
        if (_resizing) return;
        _overResizeEdge = false;
        Cursor = Cursors.Arrow;
    }

    /// <summary>根据 _collapsed / _onRight / _expandedWidth 统一套用侧边栏布局。</summary>
    private void ApplySidebarLayout()
    {
        Top = 0;
        if (_collapsed)
        {
            Width = 38;
            Height = 30;  // 折叠只剩顶部栏
            MainBorder.Width = 38;
            BtnCollapse.Content = _onRight ? "▶" : "◀";
            HeaderBlock.Text = "";
            BtnSideToggle.Visibility = Visibility.Collapsed;
            BtnSettingsToggle.Visibility = Visibility.Collapsed;
            BtnResizeLock.Visibility = Visibility.Collapsed;
            ContentScroll.Visibility = Visibility.Collapsed;
        }
        else
        {
            // 兜底：展开态宽度不允许低于可用下限，否则整个侧边栏会被压成一条
            if (_expandedWidth < MinSidebarWidth) _expandedWidth = MinSidebarWidth;
            Width = _expandedWidth;
            MainBorder.Width = double.NaN;
            BtnCollapse.Content = _onRight ? "◀" : "▶";
            HeaderBlock.Text = "侧边栏";
            BtnSideToggle.Visibility = Visibility.Visible;
            BtnSettingsToggle.Visibility = Visibility.Visible;
            BtnResizeLock.Visibility = Visibility.Visible;
            ContentScroll.Visibility = Visibility.Visible;
            AdjustHeight();  // 展开时按内容实际高度收短
        }
        Left = _onRight ? SystemParameters.PrimaryScreenWidth - Width : 0;
        MainBorder.CornerRadius = _collapsed
            ? (_onRight ? new CornerRadius(0, 0, 0, 8) : new CornerRadius(0, 0, 8, 0))
            : new CornerRadius(0, 0, 0, 12);
    }

    /// <summary>按内容实际高度设置窗口高度：删除快捷入口后侧边栏收短，不再全屏；内容超长时上限为屏幕高度。</summary>
    private void AdjustHeight()
    {
        if (_collapsed) { Height = 30; return; }
        var w = ContentScroll.ActualWidth > 0 ? ContentScroll.ActualWidth : _expandedWidth;
        ContentScroll.Measure(new Size(w, double.PositiveInfinity));
        var contentH = ContentScroll.DesiredSize.Height;
        Height = Math.Max(120, Math.Min(30 + contentH + 8, SystemParameters.PrimaryScreenHeight));
    }

    private void ToggleSide()
    {
        _onRight = !_onRight;
        ApplySidebarLayout();
        _state.Config.Sidebar.OnRight = _onRight;
        _state.Save();
    }

    private void ToggleCollapse()
    {
        _collapsed = !_collapsed;
        ApplySidebarLayout();
        _state.Config.Sidebar.Collapsed = _collapsed;
        _state.Save();
    }

    // ============ 标签管理 ============

    private void AddLabel()
    {
        _state.Config.Labels.Add(new ColorLabel { Text = "新标签", BgColor = "#3A6EA5" });
        _state.Save();
        RebuildLabels();
    }

    private void RebuildLabels()
    {
        LabelsList.Items.Clear();
        foreach (var l in _state.Config.Labels)
        {
            var fs = l.FontSize > 0 ? l.FontSize : 13;
            // 使用 Emoji.Wpf.TextBlock 替代普通 TextBlock，原生支持彩色 Emoji
            var tb = new Emoji.Wpf.TextBlock
            {
                Text = l.Text,
                FontSize = fs,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            var border = new Border
            {
                Background = ParseHexBrush(l.BgColor),
                CornerRadius = new CornerRadius(4),
                Child = tb,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand,
                Tag = l,
            };
            // 右键编辑（颜色/字号/删除）
            border.MouseRightButtonDown += (_, _) => EditLabel(l);
            LabelsList.Items.Add(border);
        }
        AdjustHeight();
    }

    // ============ 待办管理 ============

    private void AddTodo()
    {
        _state.Config.Todos.Add(new TodoItem { Text = "新待办" });
        _state.Save();
        RebuildTodo();
    }

    private void RebuildTodo()
    {
        TodoList.Items.Clear();
        foreach (var t in _state.Config.Todos)
        {
            var border = new Border
            {
                Background = ParseHexBrush(t.BgColor ?? "#3A3A4A"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 0, 3),
                Tag = t,
            };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cb = new CheckBox { IsChecked = t.Done, VerticalAlignment = VerticalAlignment.Center };
            cb.Checked += (_, _) => { t.Done = true; _state.Save(); };
            cb.Unchecked += (_, _) => { t.Done = false; _state.Save(); };

            var rtb = new Emoji.Wpf.RichTextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = ParseHexBrush(t.TextColor ?? "#FFFFFF"),
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = t.FontSize > 0 ? t.FontSize : 13,
                AcceptsReturn = false,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            };
            new System.Windows.Documents.TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd).Text = t.Text;
            rtb.TextChanged += (_, _) =>
            {
                var tr = new System.Windows.Documents.TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
                t.Text = tr.Text;
                _state.Save();
            };

            border.MouseRightButtonDown += (_, _) => EditTodo(t);

            var del = new Button
            {
                Content = "✕", Width = 22, Height = 22,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = Brushes.Gray, FontSize = 10,
                ToolTip = "删除待办",
            };
            del.Click += (_, _) => { _state.Config.Todos.Remove(t); _state.Save(); RebuildTodo(); };

            Grid.SetColumn(cb, 0);
            Grid.SetColumn(rtb, 1);
            Grid.SetColumn(del, 2);
            row.Children.Add(cb);
            row.Children.Add(rtb);
            row.Children.Add(del);
            border.Child = row;
            TodoList.Items.Add(border);
        }
        AdjustHeight();
    }

    private void EditTodo(TodoItem t)
    {
        var popup = new Window
        {
            Title = "编辑待办",
            Width = 280, Height = 340,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };
        var panel = new StackPanel { Margin = new Thickness(12) };

        panel.Children.Add(new TextBlock { Text = "背景颜色", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 6) });
        var swatchGrid = new WrapPanel();
        foreach (var c in LabelColors)
        {
            var sw = new Border { Width = 32, Height = 32, Margin = new Thickness(2), CornerRadius = new CornerRadius(4),
                Background = ParseHexBrush(c), Cursor = Cursors.Hand, Tag = c };
            sw.MouseLeftButtonDown += (s, _) => { t.BgColor = (string)((Border)s!).Tag; _state.Save(); RebuildTodo(); };
            swatchGrid.Children.Add(sw);
        }
        panel.Children.Add(swatchGrid);

        panel.Children.Add(new TextBlock { Text = "文字颜色", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 6) });
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var pair in new[] { ("白", "#FFFFFF"), ("黑", "#000000"), ("黄", "#FFD700"), ("青", "#00BCD4") })
        {
            var btn = new Button { Content = pair.Item1, Width = 40, Height = 28, Margin = new Thickness(2),
                Background = ParseHexBrush(pair.Item2),
                Foreground = pair.Item1 == "白" ? Brushes.Black : Brushes.White,
                BorderThickness = new Thickness(1), Tag = pair.Item2 };
            btn.Click += (s, _) => { t.TextColor = (string)((Button)s!).Tag; _state.Save(); RebuildTodo(); };
            colorRow.Children.Add(btn);
        }
        panel.Children.Add(colorRow);

        var fs = (double)(t.FontSize > 0 ? t.FontSize : 13);
        panel.Children.Add(new TextBlock { Text = $"字号 ({fs})", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 4) });
        var fl = (TextBlock)panel.Children[^1];
        var fsSlider = new Slider { Minimum = 8, Maximum = 48, Value = fs, TickFrequency = 1, IsSnapToTickEnabled = true };
        fsSlider.ValueChanged += (_, _) => { t.FontSize = (int)fsSlider.Value; fl.Text = $"字号 ({t.FontSize})"; _state.Save(); RebuildTodo(); };
        panel.Children.Add(fsSlider);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        var del = new Button { Content = "删除", Width = 90, Height = 32, Margin = new Thickness(0, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x8B, 0x3A, 0x3A)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        del.Click += (_, _) => { _state.Config.Todos.Remove(t); _state.Save(); RebuildTodo(); popup.Close(); };
        var done = new Button { Content = "完成", Width = 70, Height = 32,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x6E, 0xA5)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        done.Click += (_, _) => popup.Close();
        btnRow.Children.Add(del);
        btnRow.Children.Add(done);
        panel.Children.Add(btnRow);

        popup.Content = panel;
        popup.ShowDialog();
    }

    // ============ 标签颜色/字号编辑 ============

    private static readonly string[] LabelColors = {
        "#E74C3C","#E67E22","#F1C40F","#2ECC71","#1ABC9C","#3498DB","#9B59B6","#34495E",
        "#E91E63","#FF5722","#FFC107","#4CAF50","#00BCD4","#2196F3","#673AB7","#607D8B",
        "#F44336","#FF9800","#8BC34A","#3F51B5",
    };

    private void EditLabel(ColorLabel l)
    {
        var popup = new Window
        {
            Title = "编辑标签",
            Width = 280, Height = 410,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };
        var panel = new StackPanel { Margin = new Thickness(12) };
        var fs = (double)(l.FontSize > 0 ? l.FontSize : 13);

        // 文字编辑
        panel.Children.Add(new TextBlock { Text = "标签文字", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 4) });
        var textBox = new TextBox { Text = l.Text, Margin = new Thickness(0, 0, 0, 8) };
        textBox.TextChanged += (_, _) => { l.Text = textBox.Text; _state.Save(); RebuildLabels(); };
        panel.Children.Add(textBox);

        // 自定义色号
        var hexBox = new TextBox { Text = l.BgColor ?? "#3A6EA5", Margin = new Thickness(0, 0, 0, 6) };

        // 色板
        panel.Children.Add(new TextBlock { Text = "选择颜色", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 6) });
        var swatchGrid = new WrapPanel();
        foreach (var c in LabelColors)
        {
            var sw = new Border { Width = 32, Height = 32, Margin = new Thickness(2), CornerRadius = new CornerRadius(4),
                Background = ParseHexBrush(c), Cursor = Cursors.Hand, Tag = c };
            sw.MouseLeftButtonDown += (s, _) =>
            {
                l.BgColor = (string)((Border)s!).Tag;
                hexBox.Text = l.BgColor;
                _state.Save();
                RebuildLabels(); // 实时预览
            };
            swatchGrid.Children.Add(sw);
        }
        panel.Children.Add(swatchGrid);

        // 自定义色号输入
        panel.Children.Add(new TextBlock { Text = "或输入色号 (#RRGGBB) Enter 确认", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 4) });
        panel.Children.Add(hexBox);
        hexBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                l.BgColor = hexBox.Text.Trim();
                _state.Save();
                RebuildLabels();
            }
        };

        // 字号滑块
        panel.Children.Add(new TextBlock { Text = $"字号 ({fs})", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 4) });
        var fontSizeLabel = (TextBlock)panel.Children[^1];
        var fontSizeSlider = new Slider { Minimum = 8, Maximum = 48, Value = fs, TickFrequency = 1, IsSnapToTickEnabled = true };
        fontSizeSlider.ValueChanged += (_, _) =>
        {
            l.FontSize = (int)fontSizeSlider.Value;
            fontSizeLabel.Text = $"字号 ({l.FontSize})";
            _state.Save();
            RebuildLabels(); // 实时预览
        };
        panel.Children.Add(fontSizeSlider);

        // 删除 + 关闭
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        var del = new Button { Content = "删除标签", Width = 90, Height = 32, Margin = new Thickness(0, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x8B, 0x3A, 0x3A)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        del.Click += (_, _) => { _state.Config.Labels.Remove(l); _state.Save(); RebuildLabels(); popup.Close(); };
        var done = new Button { Content = "完成", Width = 70, Height = 32,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x6E, 0xA5)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        done.Click += (_, _) => popup.Close();
        btnRow.Children.Add(del);
        btnRow.Children.Add(done);
        panel.Children.Add(btnRow);

        popup.Content = panel;
        popup.ShowDialog();
    }

    // ============ 侧边栏外观 ============

    private void EditSidebarStyle()
    {
        var popup = new Window
        {
            Title = "侧边栏外观",
            Width = 280, Height = 340,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };
        var panel = new StackPanel { Margin = new Thickness(12) };

        // 自定义色号（提前声明）
        var hexBox = new TextBox { Text = "#2B2B3A", Margin = new Thickness(0, 0, 0, 6) };
        // 透明度滑块（提前声明，供色板 lambda 引用）
        var curOpacity = (MainBorder.Background as SolidColorBrush)?.Opacity ?? 0.94;
        var opacitySlider = new Slider { Minimum = 0.0, Maximum = 1.0, Value = curOpacity, TickFrequency = 0.05, IsSnapToTickEnabled = true };
        var opacityLabel = new TextBlock();

        panel.Children.Add(new TextBlock { Text = "背景颜色", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 6) });
        var swatchGrid = new WrapPanel();
        foreach (var c in LabelColors)
        {
            var sw = new Border { Width = 32, Height = 32, Margin = new Thickness(2), CornerRadius = new CornerRadius(4),
                Background = ParseHexBrush(c), Cursor = Cursors.Hand, Tag = c };
            sw.MouseLeftButtonDown += (s, _) =>
            {
                var color = ParseColor((string)((Border)s!).Tag);
                MainBorder.Background = new SolidColorBrush(color) { Opacity = opacitySlider.Value };
                hexBox.Text = (string)((Border)s!).Tag;
                SaveSidebarStyle();
            };
            swatchGrid.Children.Add(sw);
        }
        panel.Children.Add(swatchGrid);

        panel.Children.Add(new TextBlock { Text = "自定义色号 (#RRGGBB)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 4) });
        hexBox.Text = "#2B2B3A";
        hexBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                var color = ParseColor(hexBox.Text.Trim());
                MainBorder.Background = new SolidColorBrush(color) { Opacity = opacitySlider.Value };
                SaveSidebarStyle();
            }
        };
        panel.Children.Add(hexBox);

        // 透明度
        opacityLabel.Text = $"透明度 ({(int)(curOpacity * 100)}%)";
        opacityLabel.FontWeight = FontWeights.Bold;
        opacityLabel.Margin = new Thickness(0, 6, 0, 4);
        panel.Children.Add(opacityLabel);
        opacitySlider.ValueChanged += (_, _) =>
        {
            var alpha = opacitySlider.Value;
            opacityLabel.Text = $"透明度 ({(int)(alpha * 100)}%)";
            if (MainBorder.Background is SolidColorBrush br)
                MainBorder.Background = new SolidColorBrush(br.Color) { Opacity = alpha };
            SaveSidebarStyle();
        };
        panel.Children.Add(opacitySlider);

        var close = new Button { Content = "关闭", Width = 80, Height = 32, Margin = new Thickness(0, 12, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x6E, 0xA5)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        close.Click += (_, _) => { SaveSidebarStyle(); popup.Close(); };
        panel.Children.Add(close);

        popup.Content = panel;
        popup.ShowDialog();
    }

    /// <summary>把当前侧边栏背景色与透明度写回配置并保存。</summary>
    private void SaveSidebarStyle()
    {
        if (MainBorder.Background is SolidColorBrush br)
        {
            var c = br.Color;
            _state.Config.Sidebar.BgColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            _state.Config.Sidebar.Opacity = br.Opacity;
        }
        _state.Save();
    }

    // ============ 工具 ============

    private static Color ParseColor(string? hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex ?? "#2B2B3A")!; }
        catch { return Color.FromRgb(0x2B, 0x2B, 0x3A); }
    }

    private static Brush ParseHexBrush(string? hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex ?? "#3A6EA5")!;
            return new SolidColorBrush(c);
        }
        catch
        {
            return new SolidColorBrush(Color.FromRgb(0x3A, 0x6E, 0xA5));
        }
    }
}
