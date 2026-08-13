using Deskto.Core.Config;

namespace Deskto.Wpf.Views;

/// <summary>面板类型：文件分区 / 工作安排 / 待办事项 / 彩色标签区。</summary>
public enum PanelKind
{
    Files,
    Schedule,
    Todo,
    Labels,
}

/// <summary>把一个面板（视觉配置 + 类型 + 文件分区引用）打包传给 PartitionWindow。</summary>
public sealed class PanelModel
{
    public PanelKind Kind { get; }
    public PartitionConfig Visual { get; }
    public PartitionConfig? FileConfig { get; }

    public PanelModel(PanelKind kind, PartitionConfig visual, PartitionConfig? fileConfig = null)
    {
        Kind = kind;
        Visual = visual;
        FileConfig = fileConfig;
    }
}
