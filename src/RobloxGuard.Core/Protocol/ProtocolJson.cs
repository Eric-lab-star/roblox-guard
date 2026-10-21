using System.Text.Json;

namespace RobloxGuard.Core.Protocol;

/// <summary>줄 단위 JSON 코덱. 직렬화 결과에 개행이 없음을 보장한다.</summary>
public static class ProtocolJson
{
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonFile.Options);

    public static T? Deserialize<T>(string line) where T : class
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try { return JsonSerializer.Deserialize<T>(line, JsonFile.Options); }
        catch (JsonException) { return null; }
    }
}
