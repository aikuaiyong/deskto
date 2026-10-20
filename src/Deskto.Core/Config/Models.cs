namespace Deskto.Core.Config;

/// <summary>整个应用的根配置，持久化为 config.json。</summary>
public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public List<PartitionConfig> Partitions { get; set; } = new();
    public List<TodoItem> Todos { get; set; } = new();
    public WeeklySchedule Schedule { get; set; } = new();
    public List<ColorLabel> Labels { get; set; } = new();
    public LaunchBarConfig LaunchBar { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
    public string StickyNote { get; set; } = "";
    public string StickyNoteBgColor { get; set; } = "#3A3A4A";

    // 侧边栏外观与布局（持久化，重启后恢复）
    public SidebarStyle Sidebar { get; set; } = new();

    // 固定面板的外观/位置（数据本身在 Schedule/Todos/Labels 中）
    public PartitionConfig SchedulePanel { get; set; } = new() { Title = "工作安排", X = 20, Y = 20, Width = 320, Height = 200 };
    public PartitionConfig TodoPanel { get; set; } = new() { Title = "待办事项", X = 360, Y = 20, Width = 280, Height = 240 };
    public PartitionConfig LabelsPanel { get; set; } = new() { Title = "彩色标签区", X = 20, Y = 240, Width = 320, Height = 140 };
}

/// <summary>桌面分区（类似 Fences 的一个整理框）。</summary>
public sealed class PartitionConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "新建分区";

    // 几何
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 260;
    public double Height { get; set; } = 220;

    // 外观：标题区与内容区透明度独立
    public string TitleBgColor { get; set; } = "#3A6EA5";
    public double TitleOpacity { get; set; } = 0.85;
    public int TitleFontSize { get; set; } = 13;
    public double ContentOpacity { get; set; } = 0.45;
    public string? ContentBgColor { get; set; } = "#FFFFFF";

    // 自定义背景图（路径为空 = 使用上面的纯色背景）
    // PNG 透明通道天然生效（透明处透出桌面）；图片按「等比例缩放、完整显示」铺放，不裁剪不变形
    public string BackgroundImagePath { get; set; } = "";

    // 行为
    public bool Locked { get; set; }
}

public sealed class TodoItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public int FontSize { get; set; } = 13;
    public string BgColor { get; set; } = "#3A3A4A";
    public string TextColor { get; set; } = "#FFFFFF";
}

/// <summary>周视图：周一~周日 各一栏可编辑文本。</summary>
public sealed class WeeklySchedule
{
    public string Monday { get; set; } = "";
    public string Tuesday { get; set; } = "";
    public string Wednesday { get; set; } = "";
    public string Thursday { get; set; } = "";
    public string Friday { get; set; } = "";
    public string Saturday { get; set; } = "";
    public string Sunday { get; set; } = "";

    public string Get(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => Monday,
        DayOfWeek.Tuesday => Tuesday,
        DayOfWeek.Wednesday => Wednesday,
        DayOfWeek.Thursday => Thursday,
        DayOfWeek.Friday => Friday,
        DayOfWeek.Saturday => Saturday,
        _ => Sunday,
    };

    public void Set(DayOfWeek d, string v)
    {
        switch (d)
        {
            case DayOfWeek.Monday: Monday = v; break;
            case DayOfWeek.Tuesday: Tuesday = v; break;
            case DayOfWeek.Wednesday: Wednesday = v; break;
            case DayOfWeek.Thursday: Thursday = v; break;
            case DayOfWeek.Friday: Friday = v; break;
            case DayOfWeek.Saturday: Saturday = v; break;
            default: Sunday = v; break;
        }
    }
}

public sealed class ColorLabel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "标签";
    public double FontSize { get; set; } = 14; // 字号；标签高度随字号自适应
    public string BgColor { get; set; } = "#FFD166";
}

public sealed class LaunchItem
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>快捷启动栏配置。</summary>
public sealed class LaunchBarConfig
{
    public bool AutoHide { get; set; } = true; // 自动隐藏成线条
    public bool Pinned { get; set; }            // 点击固定展开
    public List<LaunchItem> Items { get; set; } = new();
}

public enum LayerMode
{
    Overlay,     // 置顶半透明覆盖窗（始终可用，回退模式）
    BehindIcons, // 置于桌面图标之下（实验性）
}

/// <summary>侧边栏外观与布局（持久化为 config.json，重启后恢复）。</summary>
public sealed class SidebarStyle
{
    public string BgColor { get; set; } = "#2B2B3A";
    public double Opacity { get; set; } = 0.94;
    public bool Collapsed { get; set; }
    public bool OnRight { get; set; } = true;
    public double Width { get; set; } = 292;
}

public sealed class AppSettings
{
    public LayerMode Layer { get; set; } = LayerMode.Overlay;
    public bool StartWithWindows { get; set; }
    public double DefaultTitleOpacity { get; set; } = 0.85;
    public double DefaultContentOpacity { get; set; } = 0.45;
    public bool HideDesktopIcons { get; set; }
}
