using AnimatedImage.Wpf;
using Deskto.Core.Config;
using Deskto.Core.Geometry;
using Deskto.Wpf.Services;
using Deskto.Wpf.Shell;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// 避免与 System.Windows.Media.Geometry 命名冲突
using CoreRect = Deskto.Core.Geometry.Rect;

namespace Deskto.Wpf.Views;

public partial class PartitionWindow : Window
{
    private readonly PanelModel _model;
    private readonly AppState _state;
    private readonly AppShell _shell;

    private StackPanel? _todoPanel;
    private WrapPanel? _labelsPanel;
    private System.Windows.Interop.HwndSourceHook? _bottomHook;
    private bool _restoringGeometry; // 恢复几何期间禁止触发保存，避免用旧值覆盖配置

    public PartitionWindow(PanelModel model, AppState state, AppShell shell)
    {
        InitializeComponent();
        _model = model;
        _state = state;
        _shell = shell;
        // 挂桌面层 + 防 Win+D
        WindowEx.AttachToDesktop(this);
        Loaded += OnLoaded;
        // 尺寸变化（含拖拽缩放）落到磁盘，重启后保留
        SizeChanged += (_, _) => { if (IsLoaded && !_restoringGeometry) SaveGeometry(); };
        BuildContent();
    }

    public PanelModel Model => _model;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var v = _model.Visual;
        // 恢复期间禁止触发保存：依次赋值 Left/Top/Width/Height 会触发
        // OnLocationChanged/SizeChanged，若不拦截，SaveGeometry 会把「尚未恢复的旧值」写回配置，
        // 导致重启后位置/大小被覆盖成默认值。
        _restoringGeometry = true;
        try
        {
            Left = v.X;
            Top = v.Y;
            Width = v.Width;
            Height = v.Height;
        }
        finally
        {
            _restoringGeometry = false;
        }
        // 不在启动早期立即钳制：开机自启时系统屏幕参数可能未就绪（读出来是 0），
        // 立即钳制会把分区位置错误地拉到左上角（表现为「位置被重置」）。
        // 延迟几秒等系统稳定后再钳制。
        var clampTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        clampTimer.Tick += (_, _) =>
        {
            clampTimer.Stop();
            ClampToDesktop();
        };
        clampTimer.Start();
        // Emoji 渲染需等到 Dispatcher Loaded 才有效
        Dispatcher.BeginInvoke(new Action(() => { ApplyVisual(); UpdateTitleBarHeight(); }),
            System.Windows.Threading.DispatcherPriority.Loaded);
        // WM_WINDOWPOSCHANGING 拦截——永远留在 Z 序底层
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        WindowEx.SendToBottom(hwnd);
        _bottomHook = WindowEx.PinToBottom();
        System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.AddHook(_bottomHook);
        // 应用锁定状态（锁定 = 背景化：点击穿透）
        ApplyLockState();
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

        // 内容区：优先显示自定义背景图，否则用纯色
        var path = v.BackgroundImagePath;
        if (!string.IsNullOrWhiteSpace(path) && SetImageSource(BackgroundImage, path))
        {
            // 背景图模式：底色透明，让 PNG 的透明区域直接透出桌面
            ContentArea.Background = Brushes.Transparent;
            BackgroundImage.Visibility = Visibility.Visible;
        }
        else
        {
            // 清掉背景图（含 GIF 动画状态）
            ImageBehavior.SetAnimatedSource(BackgroundImage, null);
            BackgroundImage.Source = null;
            BackgroundImage.Visibility = Visibility.Collapsed;
            // Alpha 通道替代 Opacity，图标不再雾化
            var cBg = ParseColor(v.ContentBgColor ?? "#FFFFFF");
            cBg.A = (byte)(Clamp01(v.ContentOpacity) * 255);
            ContentArea.Background = new SolidColorBrush(cBg);
        }
        ResizeGrip.Visibility = v.Locked ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// 把图片设置到 Image 控件：
    /// - GIF / APNG → 交给 AnimatedImage 处理（是动画就循环播放，静态图就直接显示）
    /// - JPG / BMP 等 → 静态显示
    /// 一律「等比例缩放、完整显示」：不裁剪、不变形。
    /// 关键点：加载后立即释放文件句柄，用户可随意删除/替换源图片。
    /// </summary>
    private static bool SetImageSource(Image target, string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

            var ext = Path.GetExtension(path);
            bool maybeAnimated =
                ext.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".apng", StringComparison.OrdinalIgnoreCase);

            if (maybeAnimated)
            {
                // PNG 既可能是静态图也可能是 APNG，统一交给 AnimatedImage 判断
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;   // 立即加载并释放文件句柄
                bmp.EndInit();
                ImageBehavior.SetAnimatedSource(target, bmp);
                ImageBehavior.SetRepeatBehavior(target, RepeatBehavior.Forever);
            }
            else
            {
                // 其余静态图：走缓存，避免重复解码
                var img = LoadCachedImage(path);
                if (img == null) return false;
                ImageBehavior.SetAnimatedSource(target, null);  // 清掉残留的动画状态
                target.Source = img;
            }
            target.Stretch = Stretch.Uniform;
            return true;
        }
        catch
        {
            return false; // 图片损坏/被占用等，回退纯色
        }
    }

    // 背景图缓存：同一路径且文件未修改时复用已解码的图片，避免重复读盘解码
    private static readonly Dictionary<string, (DateTime WriteTime, ImageSource Image)> _bgImageCache = new();

    private static ImageSource? LoadCachedImage(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return null;

            if (_bgImageCache.TryGetValue(path, out var cached) && cached.WriteTime == fi.LastWriteTimeUtc)
                return cached.Image;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;      // 立即加载并释放文件句柄
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.EndInit();
            bmp.Freeze();                                     // 冻结后可跨线程复用

            _bgImageCache[path] = (fi.LastWriteTimeUtc, bmp);
            return bmp;
        }
        catch
        {
            return null; // 图片损坏/被占用等，回退纯色
        }
    }

    /// <summary>背景图设置窗：选择图片、预览、清除。图片按等比例缩放完整显示。</summary>
    private void OpenBackgroundSettings()
    {
        var v = _model.Visual;
        var popup = new Window
        {
            Title = $"背景图 - {v.Title}",
            Width = 390,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };
        var panel = new StackPanel { Margin = new Thickness(12) };

        // 预览区
        var preview = new Border
        {
            Height = 170,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x28)),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 8),
        };
        var previewImg = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(4) };
        preview.Child = previewImg;
        panel.Children.Add(preview);

        // 当前路径
        var pathText = new TextBlock
        {
            FontSize = 11,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(pathText);

        // 刷新预览 + 应用到分区 + 落盘
        void RefreshPreview()
        {
            pathText.Text = string.IsNullOrWhiteSpace(v.BackgroundImagePath)
                ? "（当前使用纯色背景）"
                : v.BackgroundImagePath;
            if (string.IsNullOrWhiteSpace(v.BackgroundImagePath))
            {
                ImageBehavior.SetAnimatedSource(previewImg, null);
                previewImg.Source = null;
            }
            else
            {
                SetImageSource(previewImg, v.BackgroundImagePath);   // GIF 在预览里也会动
            }
            ApplyVisual();
            _state.Save();
        }

        // 选择图片
        var pickBtn = new Button
        {
            Content = "\U0001F4C1 选择图片（PNG / APNG / JPG / GIF / BMP）",
            Height = 34,
            Margin = new Thickness(0, 0, 0, 10),
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x6E, 0xA5)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
        };
        pickBtn.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择分区背景图",
                Filter = "图片文件|*.png;*.apng;*.jpg;*.jpeg;*.bmp;*.gif|PNG / APNG|*.png;*.apng|GIF|*.gif|所有文件|*.*",
            };
            if (dlg.ShowDialog() == true)
            {
                v.BackgroundImagePath = dlg.FileName;
                RefreshPreview();
            }
        };
        panel.Children.Add(pickBtn);

        // 说明
        panel.Children.Add(new TextBlock
        {
            Text = "图片按分区比例等比缩放后完整显示（不裁剪、不变形）。\nPNG 透明处直接透出桌面；GIF 与 APNG 会自动循环播放动画。",
            FontSize = 11,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });

        // 底部按钮
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var clearBtn = new Button
        {
            Content = "清除背景图",
            Width = 100,
            Height = 32,
            Margin = new Thickness(0, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x8B, 0x3A, 0x3A)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
        };
        clearBtn.Click += (_, _) =>
        {
            v.BackgroundImagePath = "";
            RefreshPreview();
        };
        var okBtn = new Button
        {
            Content = "完成",
            Width = 80,
            Height = 32,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x6E, 0xA5)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
        };
        okBtn.Click += (_, _) => popup.Close();
        btnRow.Children.Add(clearBtn);
        btnRow.Children.Add(okBtn);
        panel.Children.Add(btnRow);

        popup.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        RefreshPreview();
        popup.ShowDialog();
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
        // 自定义背景图
        var miBg = new MenuItem { Header = "🖼 更换背景图" };
        miBg.Click += (_, _) => OpenBackgroundSettings();
        cm.Items.Add(miBg);
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
        ApplyLockState();
        _state.Save();
    }

    /// <summary>应用锁定状态：锁定后窗口点击穿透（鼠标事件全部落到桌面），
    /// 视觉上保留标题 + 背景，但彻底不影响桌面操作；解锁恢复可交互。</summary>
    private void ApplyLockState()
    {
        var locked = _model.Visual.Locked;
        ResizeGrip.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        WindowEx.SetClickThrough(hwnd, locked);
    }

    /// <summary>外部（侧边栏/托盘）设置锁定状态：锁定后点击穿透，无法从分区自身解锁。</summary>
    public void SetLocked(bool locked)
    {
        _model.Visual.Locked = locked;
        ApplyLockState();
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
        if (!string.IsNullOrEmpty(t) && t != _model.Visual.Title)
            _model.Visual.Title = t;
        TitleEdit.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        TitleText.Text = _model.Visual.Title;
        _state.Save();
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
        if (IsLoaded && !_restoringGeometry)
        {
            ClampToDesktop();
            SaveGeometry();
        }
        base.OnLocationChanged(e);
    }

    /// <summary>限制窗口不能完全拖出桌面，确保标题栏至少 30px 可见（避免锁按钮跑到屏幕外无法解锁）。
    /// 使用虚拟屏幕边界（跨多显示器），避免副屏分区被错误拉回主屏。</summary>
    private void ClampToDesktop()
    {
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var width = SystemParameters.VirtualScreenWidth;
        var height = SystemParameters.VirtualScreenHeight;

        // 系统屏幕参数未就绪（开机早期显示器未枚举完）时跳过钳制，
        // 避免把分区位置错误地拉到左上角（表现为「位置被重置」）。
        if (width <= 0 || height <= 0)
            return;

        var right = left + width;
        var bottom = top + height;

        if (Left < left - Width + 30) Left = left - Width + 30;
        if (Left > right - 30) Left = right - 30;
        if (Top < top) Top = top;
        if (Top > bottom - 30) Top = bottom - 30;
    }

    private void SaveGeometry()
    {
        if (_restoringGeometry) return;
        var v = _model.Visual;
        v.X = Left;
        v.Y = Top;
        v.Width = Width;
        v.Height = Height;
        _state.Save();
    }

    // ---------- 内容构建（按类型切换） ----------
    private void BuildContent()
    {
        ContentHost.Content = _model.Kind switch
        {
            // 文件分区 = 纯背景色块：内容区背景色（ContentArea 的 Background）即分区背景
            PanelKind.Files => null,
            PanelKind.Schedule => BuildSchedule(),
            PanelKind.Todo => BuildTodo(),
            PanelKind.Labels => BuildLabels(),
            _ => null,
        };
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
