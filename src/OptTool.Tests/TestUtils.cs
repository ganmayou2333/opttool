using Microsoft.Win32;

namespace OptTool.Tests;

/// <summary>
/// 测试用临时注册表键：在 HKCU\Software\OptToolTest\&lt;随机&gt; 下创建，
/// Dispose 时递归删除，保证测试之间互不污染。
/// </summary>
public sealed class TempRegistryKey : IDisposable
{
    public string FullPath { get; }
    private readonly RegistryKey _root;
    private readonly string _subKey;
    private bool _disposed;

    public TempRegistryKey(string? name = null)
    {
        string leaf = name ?? Guid.NewGuid().ToString("N");
        _root = Registry.CurrentUser;
        _subKey = $@"Software\OptToolTest\{leaf}";
        FullPath = $@"HKCU\{_subKey}";
        _root.CreateSubKey(_subKey, writable: true)?.Dispose();
    }

    public RegistryKey Open() => _root.CreateSubKey(_subKey, writable: true)!;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _root.DeleteSubKeyTree(_subKey, throwOnMissingSubKey: false);
        }
        catch
        {
            // 清理失败不影响测试断言。
        }
    }
}
