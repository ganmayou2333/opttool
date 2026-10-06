using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace OptTool.Services;

/// <summary>
/// 注册表值的可序列化表示。用于 BackupService 快照与 OperationHistory 回滚。
/// 不使用 reg export，全部通过 Registry API 逐值读取后自行序列化。
/// </summary>
internal static class RegistryValueCodec
{
    public static string KindToString(RegistryValueKind kind) => kind.ToString();

    public static RegistryValueKind ParseKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return RegistryValueKind.Unknown;
        return Enum.TryParse<RegistryValueKind>(kind, ignoreCase: true, out var k)
            ? k
            : RegistryValueKind.Unknown;
    }

    /// <summary>把注册表值序列化为可写入 JSON 的字符串。</summary>
    public static string? Serialize(object? value, RegistryValueKind kind)
    {
        if (value == null) return null;
        return kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString => value.ToString(),
            RegistryValueKind.DWord => Convert.ToInt32(value).ToString(),
            RegistryValueKind.QWord => Convert.ToInt64(value).ToString(),
            RegistryValueKind.MultiString => JsonSerializer.Serialize((string[])value),
            RegistryValueKind.Binary => Convert.ToBase64String((byte[])value),
            RegistryValueKind.None => Convert.ToBase64String((byte[])value),
            _ => value.ToString(),
        };
    }

    /// <summary>把字符串反序列化为可写回注册表的对象与类型。</summary>
    public static (object? Value, RegistryValueKind Kind) Deserialize(string? stored, string? kind)
    {
        RegistryValueKind k = ParseKind(kind);
        if (stored == null) return (null, k);

        object? value = k switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString => stored,
            RegistryValueKind.DWord => int.TryParse(stored, out var d) ? d : 0,
            RegistryValueKind.QWord => long.TryParse(stored, out var q) ? q : 0L,
            RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(stored) ?? Array.Empty<string>(),
            RegistryValueKind.Binary => Convert.FromBase64String(stored),
            RegistryValueKind.None => Array.Empty<byte>(),
            _ => stored,
        };
        return (value, k);
    }
}
