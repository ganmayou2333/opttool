using Microsoft.Win32;
using OptTool.Services;
using Xunit;

namespace OptTool.Tests;

public class BackupServiceTests
{
    [Fact]
    public void BackupAndRestore_RegistryValue_PreservesValueAndKind()
    {
        using var temp = new TempRegistryKey();
        using var key = temp.Open();
        key.SetValue("TestDWORD", 42, RegistryValueKind.DWord);

        string tempDataDir = Path.Combine(Path.GetTempPath(), "opttool-test-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new BackupService(tempDataDir);

            // 1. 备份
            var result = service.BackupRegistryValue(temp.FullPath, "TestDWORD");
            Assert.True(result.Success);
            Assert.NotNull(result.BackupPath);
            Assert.True(File.Exists(result.BackupPath));

            // 2. 改成别的值
            key.SetValue("TestDWORD", 999, RegistryValueKind.DWord);
            Assert.Equal(999, (int)key.GetValue("TestDWORD")!);

            // 3. 还原
            bool restored = service.Restore(result.BackupPath!);
            Assert.True(restored);

            // 4. 验证值与类型完全一致
            Assert.Equal(42, (int)key.GetValue("TestDWORD")!);
            Assert.Equal(RegistryValueKind.DWord, key.GetValueKind("TestDWORD"));
        }
        finally
        {
            try { Directory.Delete(tempDataDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Restore_BackupOfNonExistentValue_DeletesValue()
    {
        using var temp = new TempRegistryKey();
        using var key = temp.Open();

        string tempDataDir = Path.Combine(Path.GetTempPath(), "opttool-test-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new BackupService(tempDataDir);

            // 1. 备份一个不存在的值（exists=false 快照）
            var result = service.BackupRegistryValue(temp.FullPath, "GhostValue");
            Assert.True(result.Success);

            // 2. 之后创建了这个值
            key.SetValue("GhostValue", "created-later", RegistryValueKind.String);
            Assert.True(key.GetValueNames().Contains("GhostValue"));

            // 3. 还原 → 原本不存在，必须删除（不是变成空值）
            bool restored = service.Restore(result.BackupPath!);
            Assert.True(restored);

            // 4. 验证值已消失
            Assert.DoesNotContain("GhostValue", key.GetValueNames());
        }
        finally
        {
            try { Directory.Delete(tempDataDir, recursive: true); } catch { }
        }
    }
}
