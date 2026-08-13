using System.Text.Encodings.Web;
using System.Text.Json;

namespace Deskto.Core.Config;

/// <summary>config.json 的加载 / 保存 / 序列化往返。</summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // 保留中文可读，避免被转义成 \uXXXX
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new AppConfig();
            var json = File.ReadAllText(path);
            return Deserialize(json);
        }
        catch
        {
            return new AppConfig();
        }
    }

    public static void Save(AppConfig cfg, string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, Serialize(cfg));
        }
        catch { /* 权限不足等静默忽略 */ }
    }

    /// <summary>序列化为字符串（便于往返测试）。</summary>
    public static string Serialize(AppConfig cfg) => JsonSerializer.Serialize(cfg, Options);

    /// <summary>反序列化（缺字段时回退默认值，不会因多余字段崩溃）。</summary>
    public static AppConfig Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new AppConfig();
        return JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
    }
}
