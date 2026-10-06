using System.Text.Json;
using Microsoft.Win32;

namespace OptTool.Services;

/// <summary>单次值变更记录（注册表或文件）。</summary>
public record ValueChange(
    string Target,
    string ValueName,
    bool OriginalExists,
    string? OriginalValue,
    string? OriginalKind,
    string? NewValue);

/// <summary>操作历史记录。</summary>
public record OperationRecord(
    int SchemaVersion,
    string Id,
    DateTimeOffset Timestamp,
    string OperationName,
    string RiskLevel,
    List<ValueChange> Changes);

/// <summary>
/// 操作历史：追加写入 data\history.jsonl，可读回，可按 operationId 回滚。
/// 回滚规则：原本存在的值写回原值与原类型；原本不存在的值删除。
/// </summary>
public class OperationHistory
{
    private readonly string _dataDir;
    public string HistoryFile => Path.Combine(_dataDir, "history.jsonl");

    public OperationHistory(string? dataDir = null)
    {
        _dataDir = string.IsNullOrWhiteSpace(dataDir)
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : dataDir;
    }

    /// <summary>追加一条操作记录到 history.jsonl。</summary>
    public void Append(OperationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Directory.CreateDirectory(_dataDir);
        string line = JsonSerializer.Serialize(record);
        File.AppendAllText(HistoryFile, line + Environment.NewLine);
    }

    /// <summary>读取全部操作记录（按写入顺序）。</summary>
    public IReadOnlyList<OperationRecord> ReadAll()
    {
        var list = new List<OperationRecord>();
        if (!File.Exists(HistoryFile)) return list;

        foreach (var line in File.ReadAllLines(HistoryFile))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var rec = JsonSerializer.Deserialize<OperationRecord>(line);
                if (rec != null) list.Add(rec);
            }
            catch
            {
                // 损坏行跳过，不影响整体。
            }
        }
        return list;
    }

    /// <summary>
    /// 按 operationId 回滚：逆向遍历 Changes，写回原值或删除。
    /// 全部成功返回 true；任一失败返回 false（已成功的项不回退）。
    /// </summary>
    public bool Rollback(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId)) return false;

        var records = ReadAll();
        var record = records.FirstOrDefault(r => r.Id == operationId);
        if (record == null) return false;

        bool allOk = true;
        // 逆向还原
        for (int i = record.Changes.Count - 1; i >= 0; i--)
        {
            var change = record.Changes[i];
            try
            {
                if (!RollbackOneChange(change))
                    allOk = false;
            }
            catch
            {
                allOk = false;
            }
        }
        return allOk;
    }

    private static bool RollbackOneChange(ValueChange change)
    {
        var (root, subKey) = RegistryKeyParser.Parse(change.Target);
        using var key = root.CreateSubKey(subKey, writable: true);
        if (key == null) return false;

        if (change.OriginalExists)
        {
            var (val, kind) = RegistryValueCodec.Deserialize(change.OriginalValue, change.OriginalKind);
            if (val != null)
            {
                key.SetValue(change.ValueName, val, kind);
            }
            else
            {
                key.SetValue(change.ValueName, Array.Empty<byte>(), RegistryValueKind.None);
            }
        }
        else
        {
            // 原本不存在 → 删除该值（不是写成空值）。
            key.DeleteValue(change.ValueName, throwOnMissingValue: false);
        }
        return true;
    }
}
