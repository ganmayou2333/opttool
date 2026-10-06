using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using OptTool.Interop;
using OptTool.Models;

namespace OptTool.Services;

/// <summary>
/// 系统信息采集服务。全部使用原生 API 与注册表，禁止 WMI / PowerShell。
/// 每个方法独立 try/catch，单项失败返回"不可用"占位，不抛异常。
/// 普通权限下所有方法安全返回（只读）。
/// </summary>
public class SystemInfoService
{
    /// <summary>CPU 架构、逻辑处理器数、页大小、型号、标称频率。</summary>
    public CpuInfo GetCpu()
    {
        try
        {
            NativeMethods.GetSystemInfo(out var si);
            string arch = si.wProcessorArchitecture switch
            {
                NativeMethods.PROCESSOR_ARCHITECTURE_INTEL => "x86",
                NativeMethods.PROCESSOR_ARCHITECTURE_AMD64 => "x64",
                NativeMethods.PROCESSOR_ARCHITECTURE_ARM => "ARM",
                NativeMethods.PROCESSOR_ARCHITECTURE_ARM64 => "ARM64",
                NativeMethods.PROCESSOR_ARCHITECTURE_IA64 => "IA64",
                _ => "Unknown",
            };

            string model = "未知";
            int frequencyMHz = 0;
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", writable: false);
                if (key != null)
                {
                    model = (key.GetValue("ProcessorNameString") as string)?.Trim() ?? "未知";
                    var mhz = key.GetValue("~MHz");
                    if (mhz != null) int.TryParse(mhz.ToString(), out frequencyMHz);
                }
            }
            catch
            {
                // 注册表读取失败不影响整体，保留默认值。
            }

            return new CpuInfo(arch, (int)si.dwNumberOfProcessors, (int)si.dwPageSize, model, frequencyMHz);
        }
        catch
        {
            return new CpuInfo("不可用", 0, 0, "不可用", 0);
        }
    }

    /// <summary>物理内存总量、可用量、占用率百分比。</summary>
    public MemoryInfo GetMemory()
    {
        try
        {
            var mem = new NativeMethods.MEMORYSTATUSEX
            {
                dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>(),
            };
            if (NativeMethods.GlobalMemoryStatusEx(ref mem))
            {
                return new MemoryInfo((long)mem.ullTotalPhys, (long)mem.ullAvailPhys, mem.dwMemoryLoad);
            }
            return new MemoryInfo(0, 0, 0);
        }
        catch
        {
            return new MemoryInfo(0, 0, 0);
        }
    }

    /// <summary>
    /// 操作系统信息。注意：注册表 ProductName 在 Win11 上仍可能写 "Windows 10"，
    /// 必须用 CurrentBuildNumber >= 22000 判定为 Windows 11 并覆盖产品名。
    /// </summary>
    public OsInfo GetOs()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false);
            if (key == null)
                return new OsInfo("不可用", string.Empty, string.Empty, string.Empty, string.Empty);

            string productName = (key.GetValue("ProductName") as string) ?? string.Empty;
            string build = (key.GetValue("CurrentBuildNumber") as string) ?? string.Empty;
            string version = (key.GetValue("DisplayVersion") as string)
                ?? (key.GetValue("ReleaseId") as string)
                ?? string.Empty;
            string ubr = key.GetValue("UBR")?.ToString() ?? string.Empty;
            string editionId = (key.GetValue("EditionID") as string) ?? string.Empty;

            if (int.TryParse(build, out int buildNumber) && buildNumber >= 22000)
            {
                productName = "Windows 11";
            }

            return new OsInfo(productName, version, build, ubr, editionId);
        }
        catch
        {
            return new OsInfo("不可用", string.Empty, string.Empty, string.Empty, string.Empty);
        }
    }

    /// <summary>所有就绪的固定/可移动磁盘卷信息。</summary>
    public IReadOnlyList<DiskInfo> GetDisks()
    {
        var list = new List<DiskInfo>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;
                    list.Add(new DiskInfo(
                        drive.Name.TrimEnd('\\'),
                        drive.VolumeLabel,
                        drive.TotalSize,
                        drive.AvailableFreeSpace));
                }
                catch
                {
                    // 单个卷读取失败跳过，不影响整体。
                }
            }
        }
        catch
        {
            // 枚举失败返回已收集部分（可能为空）。
        }
        return list;
    }

    /// <summary>
    /// 启动项：HKCU/HKLM 的 Run 键 + 当前用户与公共启动文件夹。
    /// </summary>
    public IReadOnlyList<StartupEntry> GetStartupEntries()
    {
        var list = new List<StartupEntry>();
        try
        {
            AddRunEntries(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "注册表 HKCU Run", list);
            AddRunEntries(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "注册表 HKLM Run", list);

            try
            {
                string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                AddStartupFolderEntries(startup, "启动文件夹", list);
            }
            catch { }

            try
            {
                string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
                AddStartupFolderEntries(commonStartup, "启动文件夹(公共)", list);
            }
            catch { }
        }
        catch
        {
            // 整体失败返回已收集部分。
        }
        return list;
    }

    private static void AddRunEntries(RegistryKey root, string subKey, string source, List<StartupEntry> list)
    {
        try
        {
            using var key = root.OpenSubKey(subKey, writable: false);
            if (key == null) return;
            foreach (var name in key.GetValueNames())
            {
                if (string.IsNullOrEmpty(name)) continue;
                var command = key.GetValue(name) as string ?? string.Empty;
                list.Add(new StartupEntry(name, command, source));
            }
        }
        catch
        {
            // 单个键读取失败跳过。
        }
    }

    private static void AddStartupFolderEntries(string folder, string source, List<StartupEntry> list)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        foreach (var file in Directory.GetFiles(folder))
        {
            list.Add(new StartupEntry(Path.GetFileName(file), file, source));
        }
    }

    /// <summary>当前进程提权状态。未提权时 CanWriteSystemSettings=false 并给出原因。</summary>
    public ElevationStatus GetElevation()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            bool isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            return new ElevationStatus(
                isAdmin,
                isAdmin,
                isAdmin ? "已以管理员身份运行" : "普通权限，写入系统设置需以管理员身份重新启动");
        }
        catch
        {
            return new ElevationStatus(false, false, "无法检测提权状态");
        }
    }
}
