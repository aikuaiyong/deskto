using Deskto.Core.Config;
using System.IO;

namespace Deskto.Wpf.Services;

/// <summary>持有当前配置并提供保存。各窗口共享同一实例。</summary>
public sealed class AppState
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "deskto", "config.json");

    public AppConfig Config { get; private set; } = new();
    public string ConfigPath { get; }

    /// <summary>是否首次运行（配置文件尚不存在）。用于初始化示例分区与启动提示。</summary>
    public bool IsFirstRun { get; }

    public AppState(string? configPath = null)
    {
        ConfigPath = configPath ?? DefaultPath;
        IsFirstRun = !File.Exists(ConfigPath);
        Config = ConfigStore.Load(ConfigPath);
    }

    public void Save() => ConfigStore.Save(Config, ConfigPath);
}
