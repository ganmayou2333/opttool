using Microsoft.Win32;

namespace OptTool.Services;

/// <summary>
/// 把 "HKLM\SOFTWARE\..." / "HKEY_LOCAL_MACHINE\..." 形式的注册表路径解析为
/// (RegistryKey root, string subKey)。供 BackupService / OperationHistory 共用。
/// </summary>
internal static class RegistryKeyParser
{
    public static (RegistryKey root, string subKey) Parse(string keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
            throw new ArgumentException("注册表路径不能为空", nameof(keyPath));

        string normalized = keyPath.Replace('/', '\\').TrimStart('\\');
        int firstSep = normalized.IndexOf('\\');
        string rootName = firstSep < 0 ? normalized : normalized.Substring(0, firstSep);
        string subKey = firstSep < 0 ? string.Empty : normalized.Substring(firstSep + 1);

        RegistryKey root = rootName.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            "HKU" or "HKEY_USERS" => Registry.Users,
            "HKCC" or "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
            _ => throw new ArgumentException($"不支持的注册表根键: {rootName}", nameof(keyPath)),
        };

        return (root, subKey);
    }
}
