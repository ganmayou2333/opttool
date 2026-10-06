using System.Text.Json;
using Microsoft.Win32;

namespace OptTool.Services;

/// <summary>注册表值备份结果。</summary>
public record RegistryBackupResult(bool Success, string? BackupPath, string? Error);

/// <summary>
/// 备份与还原服务。
/// - 注册表值：逐值读取后自行序列化为 JSON 快照（不使用 reg export）。
/// - 文件：移动到 data\rescue\&lt;时间戳&gt;\ 下，并写 sidecar 记录原路径。
/// 数据目录默认 AppContext.BaseDirectory\data（禁止用程序集路径定位），
/// 可通过构造函数注入以便单元测试隔离。
/// </summary>
public class BackupService
{
    private readonly string _dataDir;

    public string DataDir => _dataDir;
    public string BackupDir => Path.Combine(_dataDir, "backup");
    public string RescueDir => Path.Combine(_dataDir, "rescue");

    public BackupService(string? dataDir = null)
    {
        _dataDir = string.IsNullOrWhiteSpace(dataDir)
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : dataDir;
    }

    /// <summary>
    /// 备份单个注册表值。快照记录：值是否存在(Exists)、原始类型(Kind)、原始值。
    /// 即使值不存在也会生成一份 exists=false 的快照，供回滚时删除。
    /// </summary>
    public RegistryBackupResult BackupRegistryValue(string keyPath, string valueName)
    {
        try
        {
            var (root, subKey) = RegistryKeyParser.Parse(keyPath);

            bool exists = false;
            RegistryValueKind kind = RegistryValueKind.Unknown;
            object? value = null;

            using (var key = root.OpenSubKey(subKey, writable: false))
            {
                if (key != null)
                {
                    var names = key.GetValueNames();
                    exists = names.Any(n => n.Equals(valueName, StringComparison.OrdinalIgnoreCase));
                    if (exists)
                    {
                        kind = key.GetValueKind(valueName);
                        value = key.GetValue(valueName);
                    }
                }
            }

            var snapshot = new
            {
                type = "registry-value",
                keyPath,
                valueName,
                exists,
                kind = RegistryValueCodec.KindToString(kind),
                value = RegistryValueCodec.Serialize(value, kind),
            };

            Directory.CreateDirectory(BackupDir);
            string stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff");
            string safeName = SanitizeFileName($"{stamp}_{valueName}");
            string path = Path.Combine(BackupDir, $"{safeName}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));

            return new RegistryBackupResult(true, path, null);
        }
        catch (Exception ex)
        {
            return new RegistryBackupResult(false, null, ex.Message);
        }
    }

    /// <summary>
    /// 还原。支持两种备份：
    ///   - 注册表值快照（.json，type=registry-value）：存在则写回原值与原类型，不存在则删除该值。
    ///   - 文件备份（rescue 下的文件，旁边有 .meta.json）：移回原路径。
    /// </summary>
    public bool Restore(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
            return false;

        try
        {
            // 注册表快照
            if (backupPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                string json = File.ReadAllText(backupPath);
                using var doc = JsonDocument.Parse(json);
                var rootEl = doc.RootElement;
                string? type = rootEl.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "registry-value")
                {
                    return RestoreRegistryValue(rootEl);
                }
            }

            // 文件备份：检查 sidecar
            string metaPath = backupPath + ".meta.json";
            if (File.Exists(metaPath))
            {
                string metaJson = File.ReadAllText(metaPath);
                using var doc = JsonDocument.Parse(metaJson);
                string? originalPath = doc.RootElement.TryGetProperty("originalPath", out var op) ? op.GetString() : null;
                if (!string.IsNullOrWhiteSpace(originalPath))
                {
                    string? dir = Path.GetDirectoryName(originalPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.Move(backupPath, originalPath, overwrite: true);
                    try { File.Delete(metaPath); } catch { }
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool RestoreRegistryValue(JsonElement rootEl)
    {
        try
        {
            string? keyPath = rootEl.TryGetProperty("keyPath", out var kp) ? kp.GetString() : null;
            string? valueName = rootEl.TryGetProperty("valueName", out var vn) ? vn.GetString() : null;
            bool exists = rootEl.TryGetProperty("exists", out var e) && e.GetBoolean();
            string? kind = rootEl.TryGetProperty("kind", out var kd) ? kd.GetString() : null;
            string? value = rootEl.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            if (string.IsNullOrWhiteSpace(keyPath) || string.IsNullOrEmpty(valueName))
                return false;

            var (root, subKey) = RegistryKeyParser.Parse(keyPath);
            using var key = root.CreateSubKey(subKey, writable: true);
            if (key == null) return false;

            if (exists)
            {
                var (val, k) = RegistryValueCodec.Deserialize(value, kind);
                if (val != null)
                {
                    key.SetValue(valueName, val, k);
                }
                else
                {
                    // 原存在但值为 null（None 类型），写回空二进制。
                    key.SetValue(valueName, Array.Empty<byte>(), RegistryValueKind.None);
                }
            }
            else
            {
                // 原本不存在 → 删除（不是写成空值）。
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 备份文件：移动到 data\rescue\&lt;时间戳&gt;\ 下，写 sidecar 记录原路径，返回新路径。
    /// </summary>
    public string? BackupFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;
        try
        {
            string stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff");
            string destDir = Path.Combine(RescueDir, stamp);
            Directory.CreateDirectory(destDir);
            string destPath = Path.Combine(destDir, Path.GetFileName(filePath));
            File.Move(filePath, destPath, overwrite: true);

            var meta = new { originalPath = Path.GetFullPath(filePath), backedUpAt = DateTimeOffset.Now };
            File.WriteAllText(destPath + ".meta.json", JsonSerializer.Serialize(meta));

            return destPath;
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 120 ? name.Substring(0, 120) : name;
    }
}
