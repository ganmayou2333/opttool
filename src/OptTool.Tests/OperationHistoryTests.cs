using Microsoft.Win32;
using OptTool.Services;
using Xunit;

namespace OptTool.Tests;

public class OperationHistoryTests
{
    private static string NewTempDataDir() =>
        Path.Combine(Path.GetTempPath(), "opttool-test-history-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Append_ThenReadAll_FieldsMatch()
    {
        string dataDir = NewTempDataDir();
        try
        {
            var history = new OperationHistory(dataDir);
            var record = new OperationRecord(
                SchemaVersion: 1,
                Id: "op-test-001",
                Timestamp: DateTimeOffset.Parse("2026-01-02T03:04:05Z"),
                OperationName: "测试操作",
                RiskLevel: "low",
                Changes: new List<ValueChange>
                {
                    new("HKCU\\Software\\OptToolTest\\x", "v", true, "old", "String", "new"),
                });

            history.Append(record);

            var all = history.ReadAll();
            var got = all.FirstOrDefault(r => r.Id == "op-test-001");

            Assert.NotNull(got);
            Assert.Equal(1, got!.SchemaVersion);
            Assert.Equal("测试操作", got.OperationName);
            Assert.Equal("low", got.RiskLevel);
            Assert.Single(got.Changes);
            Assert.Equal("v", got.Changes[0].ValueName);
            Assert.True(got.Changes[0].OriginalExists);
            Assert.Equal("old", got.Changes[0].OriginalValue);
            Assert.Equal("String", got.Changes[0].OriginalKind);
            Assert.Equal("new", got.Changes[0].NewValue);
        }
        finally
        {
            try { Directory.Delete(dataDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Rollback_NewValueRecord_DeletesValue()
    {
        using var temp = new TempRegistryKey();
        using var key = temp.Open();
        string dataDir = NewTempDataDir();
        try
        {
            var history = new OperationHistory(dataDir);

            // 记录一个"新建值"操作：原本不存在
            var record = new OperationRecord(
                SchemaVersion: 1,
                Id: "op-new-value",
                Timestamp: DateTimeOffset.Now,
                OperationName: "新建启动项",
                RiskLevel: "low",
                Changes: new List<ValueChange>
                {
                    new(temp.FullPath, "NewEntry", OriginalExists: false, OriginalValue: null, OriginalKind: null, NewValue: "C:\\tool.exe"),
                });
            history.Append(record);

            // 模拟操作已执行：值已创建
            key.SetValue("NewEntry", "C:\\tool.exe", RegistryValueKind.String);
            Assert.Contains("NewEntry", key.GetValueNames());

            // 回滚
            bool ok = history.Rollback("op-new-value");
            Assert.True(ok);

            // 值必须消失
            Assert.DoesNotContain("NewEntry", key.GetValueNames());
        }
        finally
        {
            try { Directory.Delete(dataDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Rollback_ChangedValueRecord_RestoresOriginal()
    {
        using var temp = new TempRegistryKey();
        using var key = temp.Open();
        string dataDir = NewTempDataDir();
        try
        {
            var history = new OperationHistory(dataDir);

            // 原值
            key.SetValue("Mode", "original", RegistryValueKind.String);

            // 记录一个"改值"操作
            var record = new OperationRecord(
                SchemaVersion: 1,
                Id: "op-change-value",
                Timestamp: DateTimeOffset.Now,
                OperationName: "修改设置",
                RiskLevel: "medium",
                Changes: new List<ValueChange>
                {
                    new(temp.FullPath, "Mode", OriginalExists: true, OriginalValue: "original", OriginalKind: "String", NewValue: "modified"),
                });
            history.Append(record);

            // 模拟操作已执行
            key.SetValue("Mode", "modified", RegistryValueKind.String);
            Assert.Equal("modified", (string)key.GetValue("Mode")!);

            // 回滚
            bool ok = history.Rollback("op-change-value");
            Assert.True(ok);

            // 恢复原值
            Assert.Equal("original", (string)key.GetValue("Mode")!);
            Assert.Equal(RegistryValueKind.String, key.GetValueKind("Mode"));
        }
        finally
        {
            try { Directory.Delete(dataDir, recursive: true); } catch { }
        }
    }
}
