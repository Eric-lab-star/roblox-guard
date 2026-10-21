using System.Text.Json;
using System.Text.Json.Serialization;

namespace RobloxGuard.Core;

/// <summary>JSON 파일 원자적 저장/로드. 없거나 깨지면 fallback.</summary>
public static class JsonFile
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static T LoadOrDefault<T>(string path, Func<T> fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback();
            var text = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(text, Options) ?? fallback();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return fallback();
        }
    }

    public static void SaveAtomic<T>(string path, T value)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        var tmp = full + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, full, overwrite: true);
    }
}
