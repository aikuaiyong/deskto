using Deskup.Core.Config;
using System.IO;

namespace Deskup.Wpf.Services;

/// <summary>持有当前配置并提供保存。各窗口共享同一实例。</summary>
public sealed class AppState
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Deskup", "config.json");

    public AppConfig Config { get; private set; } = new();
    public string ConfigPath { get; }

    public AppState(string? configPath = null)
    {
        ConfigPath = configPath ?? DefaultPath;
        Config = ConfigStore.Load(ConfigPath);
    }

    public void Save() => ConfigStore.Save(Config, ConfigPath);
}
