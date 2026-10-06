using OptTool.Services;
using Xunit;

namespace OptTool.Tests;

public class GuardListTests
{
    [Fact]
    public void DesktopPath_IsRejected()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string path = Path.Combine(desktop, "test.txt");

        var (allowed, reason) = GuardList.IsPathAllowed(path);

        Assert.False(allowed);
        Assert.False(string.IsNullOrEmpty(reason));
    }

    [Fact]
    public void OneDrivePath_IsRejected()
    {
        string oneDrive = Environment.GetEnvironmentVariable("OneDrive")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive");
        string path = Path.Combine(oneDrive, "file.txt");

        var (allowed, _) = GuardList.IsPathAllowed(path);

        Assert.False(allowed);
    }

    [Fact]
    public void RpcSsService_IsRejected()
    {
        var (allowed, _) = GuardList.IsServiceAllowed("RpcSs");
        Assert.False(allowed);
    }

    [Fact]
    public void WinDefendService_IsRejected()
    {
        var (allowed, _) = GuardList.IsServiceAllowed("WinDefend");
        Assert.False(allowed);
    }

    [Fact]
    public void WuauservDisable_IsRejected()
    {
        var (allowed, _) = GuardList.IsOperationAllowed("service:wuauserv:disable");
        Assert.False(allowed);
    }

    [Fact]
    public void WuauservPause_IsAllowed()
    {
        var (allowed, _) = GuardList.IsOperationAllowed("service:wuauserv:pause");
        Assert.True(allowed);
    }

    [Fact]
    public void BootExecuteRegistryKey_IsRejected()
    {
        var (allowed, _) = GuardList.IsRegistryKeyAllowed(
            @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\BootExecute");
        Assert.False(allowed);
    }

    [Fact]
    public void TempPath_IsAllowed()
    {
        string path = Path.Combine(Path.GetTempPath(), "opttool-test.tmp");
        var (allowed, _) = GuardList.IsPathAllowed(path);
        Assert.True(allowed);
    }

    [Fact]
    public void WindowsTempPath_IsAllowed()
    {
        var (allowed, _) = GuardList.IsPathAllowed(@"C:\Windows\Temp\opttool-test.tmp");
        Assert.True(allowed);
    }

    // ---- 缺陷 1 回归：注册表精确路径段匹配 ----

    [Fact]
    public void AdmxWinLogonKey_IsAllowed()
    {
        // 含 "WinLogon" 子串但不是 Winlogon\Shell 路径段，不得误拒。
        var (allowed, _) = GuardList.IsRegistryKeyAllowed(
            @"HKLM\SOFTWARE\Microsoft\PolicyManager\default\ADMX_WinLogon");
        Assert.True(allowed);
    }

    [Fact]
    public void WinlogonPasswordExpiryNotificationKey_IsAllowed()
    {
        // Winlogon 下的合法子键，不是 Shell，不得误拒。
        var (allowed, _) = GuardList.IsRegistryKeyAllowed(
            @"HKCU\SOFTWARE\Microsoft\Windows\Winlogon\PasswordExpiryNotification");
        Assert.True(allowed);
    }

    [Fact]
    public void WinlogonShellKey_IsRejected()
    {
        // 真正要保护的 Winlogon\Shell：连续两个路径段，必须拒绝。
        // 同时用正斜杠 + 结尾反斜杠验证边界归一化。
        var (allowed, _) = GuardList.IsRegistryKeyAllowed(
            "HKLM/SOFTWARE/Microsoft/Windows NT/CurrentVersion/Winlogon/Shell/");
        Assert.False(allowed);
    }

    [Fact]
    public void BootExecuteExactSegment_IsRejected()
    {
        // BootExecute 作为完整路径段出现时拒绝（精确段匹配，不是子串）。
        var (allowed, _) = GuardList.IsRegistryKeyAllowed(
            @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\BootExecute");
        Assert.False(allowed);
    }

    [Fact]
    public void CurrentVersionRunKey_IsAllowed()
    {
        // 普通合法键（启动项 Run 键）必须允许。
        var (allowed, _) = GuardList.IsRegistryKeyAllowed(
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run");
        Assert.True(allowed);
    }
}
