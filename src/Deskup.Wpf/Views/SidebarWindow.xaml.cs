using Deskup.Core.Config;
using Deskup.Wpf.Helpers;
using Deskup.Wpf.Services;
using Deskup.Wpf.Shell;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Deskup.Wpf.Views;

/// <summary>
/// 右侧侧边栏：五区合一
/// - 功能设置区（添加分区 / 设置 / 外观 / 退出）
/// - 彩色标签区（增删标签、编辑文字和背景色）
/// - 待办事项区（增删待办、勾选完成）
/// - 临时便签（自由格式文本）
/// - 快捷入口（4×2 网格，可自定义）
/// 支持折叠/展开、左侧拖拽调整宽度。
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
    private bool _resizeUnlocked;  // 解锁后才能拖边缘调宽度
    private System.Windows.Interop.HwndSourceHook? _bottomHook;

    public SidebarWindow(AppState state, AppShell shell)
    {
        InitializeComponent();
        _state = state;
        _shell = shell;

        // 挂桌面层（必须在 Show 之前调用）
        WindowEx.AttachToDesktop(this);

        // 功能设置按钮
        BtnAdd.Click += (_, _) => _shell.AddPartition();
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

        // 快捷入口
        BtnAddQuick.Click += (_, _) => AddQuickEntry();

        // 挂桌面层（必须在 Loaded 之前注册）
        Loaded += (_, _) =>
        {
            try
            {
                Reposition();
                RebuildLabels();
                RebuildTodo();
                StickyNote.Text = _state.Config.StickyNote;
                StickyBgBorder.Background = ParseHexBrush(_state.Config.StickyNoteBgColor);
                EnsureDefaultQuickEntries();
                RebuildQuickEntries();
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
    }

    private void StickyNote_TextChanged(object sender, TextChangedEventArgs e)
    {
        _state.Config.StickyNote = StickyNote.Text;
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
    }

    private void ToggleSettingsPanel()
    {
        SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
    }

    // 保留供 XAML 绑定（不再有 ResizeGrip，这些是空壳）
    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_collapsed || !_resizeUnlocked) return;
        var x = e.GetPosition(this).X;
        var edge = _onRight ? 6 : Width - 6;
        var nearEdge = _onRight ? (x <= edge) : (x >= edge);
        if (!nearEdge) return;
        _resizing = true;
        _resizeStartWidth = Width;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizing) return;
        var screenX = PointToScreen(e.GetPosition(this)).X;
        var maxW = SystemParameters.PrimaryScreenWidth / 3;
        double newW = _onRight
            ? SystemParameters.PrimaryScreenWidth - screenX
            : screenX;
        if (newW < 38) newW = 38;
        if (newW > maxW) newW = maxW;
        Width = newW;
        _expandedWidth = newW;
        Reposition();
    }

    private void OnWindowMouseUp(object sender, MouseButtonEventArgs e)
    {
        _resizing = false;
        ReleaseMouseCapture();
    }

    private void Reposition()
    {
        Height = SystemParameters.PrimaryScreenHeight;
        Top = 0;
        if (_onRight)
        {
            Left = SystemParameters.PrimaryScreenWidth - Width;
            MainBorder.CornerRadius = _collapsed
                ? new CornerRadius(0, 0, 0, 8)
                : new CornerRadius(0, 0, 0, 12);
        }
        else
        {
            Left = 0;
            MainBorder.CornerRadius = _collapsed
                ? new CornerRadius(0, 0, 8, 0)
                : new CornerRadius(0, 0, 12, 0);
        }
    }

    private void ToggleSide()
    {
        _onRight = !_onRight;
        Reposition();
    }

    private void ToggleCollapse()
    {
        _collapsed = !_collapsed;
        if (_collapsed)
        {
            _expandedWidth = Width;
            Width = 38;
            BtnCollapse.Content = _onRight ? "▶" : "◀";
            HeaderBlock.Text = "";
            BtnSideToggle.Visibility = Visibility.Collapsed;
            BtnSettingsToggle.Visibility = Visibility.Collapsed;
            BtnResizeLock.Visibility = Visibility.Collapsed;
            ContentScroll.Visibility = Visibility.Collapsed;
            MainBorder.Width = 38;
        }
        else
        {
            Width = _expandedWidth;
            BtnCollapse.Content = _onRight ? "◀" : "▶";
            HeaderBlock.Text = "侧边栏";
            BtnSideToggle.Visibility = Visibility.Visible;
            BtnSettingsToggle.Visibility = Visibility.Visible;
            BtnResizeLock.Visibility = Visibility.Visible;
            ContentScroll.Visibility = Visibility.Visible;
            MainBorder.Width = double.NaN;
        }
        Reposition();
    }

    // ============ 快捷入口（4×2 网格） ============

    private void EnsureDefaultQuickEntries()
    {
        if (_state.Config.QuickEntries.Count > 0) return;

        _state.Config.QuickEntries.Add(new QuickEntry
        {
            Name = "此电脑", TargetPath = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"
        });
        _state.Config.QuickEntries.Add(new QuickEntry
        {
            Name = "回收站", TargetPath = "::{645FF040-5081-101B-9F08-00AA002F954E}"
        });
        _state.Save();
    }

    private void AddQuickEntry()
    {
        var entry = new QuickEntry();
        _state.Config.QuickEntries.Add(entry);
        _state.Save();
        RebuildQuickEntries();
        EditQuickEntry(entry); // 直接弹出编辑框
    }

    private void RebuildQuickEntries()
    {
        QuickGrid.Children.Clear();
        foreach (var e in _state.Config.QuickEntries)
        {
            var btn = new Button
            {
                Width = 58, Height = 58, Margin = new Thickness(2),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                Tag = e,
            };

            // 图标 + 名称垂直排列
            var stack = new StackPanel();

            // 图标：优先用系统图标，支持自定义 IconPath
            var img = new System.Windows.Controls.Image
            {
                Width = 26, Height = 26,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            // 尝试提取图标
            if (!string.IsNullOrEmpty(e.IconPath) && File.Exists(e.IconPath))
            {
                try
                {
                    var icon = System.Drawing.Icon.ExtractAssociatedIcon(e.IconPath);
                    img.Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        icon!.Handle, Int32Rect.Empty,
                        System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                }
                catch { SetPlaceholderIcon(img, e.Name); }
            }
            else if (!string.IsNullOrEmpty(e.TargetPath))
            {
                try
                {
                    var icon = IconHelper.GetFileIcon(e.TargetPath);
                    if (icon != null)
                        img.Source = icon;
                    else
                        SetPlaceholderIcon(img, e.Name);
                }
                catch { SetPlaceholderIcon(img, e.Name); }
            }
            else
            {
                SetPlaceholderIcon(img, e.Name);
            }

            stack.Children.Add(img);

            var nameBlock = new TextBlock
            {
                Text = TruncateName(e.Name, 5),
                FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            stack.Children.Add(nameBlock);

            btn.Content = stack;
            btn.ToolTip = e.Name;

            // 左键打开
            btn.Click += (_, _) => LaunchQuickEntry(e);
            // 右键编辑
            btn.MouseRightButtonDown += (_, args) =>
            {
                args.Handled = true;
                EditQuickEntry(e);
            };

            QuickGrid.Children.Add(btn);
        }
    }

    private static void SetPlaceholderIcon(System.Windows.Controls.Image img, string name)
    {
        var tb = new TextBlock
        {
            Text = name.Length > 0 ? name[..Math.Min(1, name.Length)] : "?",
            FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x99)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        img.Source = null;
        img.Visibility = Visibility.Collapsed;
    }

    private static string TruncateName(string name, int maxChars)
    {
        if (string.IsNullOrEmpty(name)) return "?";
        return name.Length <= maxChars ? name : name[..(maxChars - 1)] + "…";
    }

    private static void LaunchQuickEntry(QuickEntry e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(e.TargetPath)) return;

            var psi = new ProcessStartInfo
            {
                FileName = e.TargetPath,
                Arguments = e.Arguments ?? "",
                UseShellExecute = true,
            };
            Process.Start(psi);
        }
        catch { /* 静默忽略 */ }
    }

    private void EditQuickEntry(QuickEntry e)
    {
        var popup = new Window
        {
            Title = "编辑快捷入口",
            Width = 320, Height = 350,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };
        var panel = new StackPanel { Margin = new Thickness(12) };

        panel.Children.Add(new TextBlock { Text = "名称", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 4) });
        var nameBox = new TextBox { Text = e.Name, Margin = new Thickness(0, 0, 0, 8) };
        nameBox.TextChanged += (_, _) => { e.Name = nameBox.Text; _state.Save(); RebuildQuickEntries(); };
        panel.Children.Add(nameBox);

        panel.Children.Add(new TextBlock { Text = "目标路径（文件 / 文件夹 / Shell: 协议）", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 4) });
        var targetRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var targetBox = new TextBox { Text = e.TargetPath };
        var browseBtn = new Button { Content = "…", Width = 30, Height = 22 };
        browseBtn.Click += (_, _) =>
        {
            // 文件选择
            using var ofd = new System.Windows.Forms.OpenFileDialog
            {
                Title = "选择目标文件",
                Filter = "所有文件|*.*|可执行文件|*.exe|快捷方式|*.lnk",
            };
            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                targetBox.Text = ofd.FileName;
        };
        DockPanel.SetDock(browseBtn, Dock.Right);
        targetRow.Children.Add(browseBtn);
        targetRow.Children.Add(targetBox);
        targetBox.TextChanged += (_, _) => { e.TargetPath = targetBox.Text; _state.Save(); RebuildQuickEntries(); };
        panel.Children.Add(targetRow);

        panel.Children.Add(new TextBlock { Text = "启动参数（可选）", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 4) });
        var argBox = new TextBox { Text = e.Arguments, Margin = new Thickness(0, 0, 0, 8) };
        argBox.TextChanged += (_, _) => { e.Arguments = argBox.Text; _state.Save(); };
        panel.Children.Add(argBox);

        panel.Children.Add(new TextBlock { Text = "自定义图标路径（可选，留空=自动提取）", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 4) });
        var iconRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var iconBox = new TextBox { Text = e.IconPath };
        var iconBrowseBtn = new Button { Content = "…", Width = 30, Height = 22 };
        iconBrowseBtn.Click += (_, _) =>
        {
            using var ofd = new System.Windows.Forms.OpenFileDialog
            {
                Title = "选择图标文件",
                Filter = "图标/可执行文件|*.ico;*.exe;*.dll|所有文件|*.*",
            };
            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                iconBox.Text = ofd.FileName;
        };
        DockPanel.SetDock(iconBrowseBtn, Dock.Right);
        iconRow.Children.Add(iconBrowseBtn);
        iconRow.Children.Add(iconBox);
        iconBox.TextChanged += (_, _) => { e.IconPath = iconBox.Text; _state.Save(); RebuildQuickEntries(); };
        panel.Children.Add(iconRow);

        // 按钮行
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        var del = new Button
        {
            Content = "删除", Width = 80, Height = 32, Margin = new Thickness(0, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x8B, 0x3A, 0x3A)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
        };
        del.Click += (_, _) =>
        {
            _state.Config.QuickEntries.Remove(e);
            _state.Save();
            RebuildQuickEntries();
            popup.Close();
        };
        var done = new Button
        {
            Content = "完成", Width = 70, Height = 32,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x6E, 0xA5)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
        };
        done.Click += (_, _) => popup.Close();
        btnRow.Children.Add(del);
        btnRow.Children.Add(done);
        panel.Children.Add(btnRow);

        popup.Content = panel;
        popup.ShowDialog();
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
        };
        panel.Children.Add(opacitySlider);

        var close = new Button { Content = "关闭", Width = 80, Height = 32, Margin = new Thickness(0, 12, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x6E, 0xA5)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        close.Click += (_, _) => popup.Close();
        panel.Children.Add(close);

        popup.Content = panel;
        popup.ShowDialog();
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
