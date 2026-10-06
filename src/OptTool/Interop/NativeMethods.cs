using System.Runtime.InteropServices;

namespace OptTool.Interop;

/// <summary>
/// Win32 原生 API 声明。全部使用 Unicode（W）变体；本文件只包含只读查询 API。
/// </summary>
internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_INFO
    {
        public ushort wProcessorArchitecture;
        public ushort wReserved;
        public uint dwPageSize;
        public IntPtr lpMinimumApplicationAddress;
        public IntPtr lpMaximumApplicationAddress;
        public IntPtr dwActiveProcessorMask;
        public uint dwNumberOfProcessors;
        public uint dwProcessorType;
        public uint dwAllocationGranularity;
        public ushort wProcessorLevel;
        public ushort wProcessorRevision;
    }

    /// <summary>
    /// 处理器架构常量，对应 SYSTEM_INFO.wProcessorArchitecture。
    /// </summary>
    public const ushort PROCESSOR_ARCHITECTURE_INTEL = 0;
    public const ushort PROCESSOR_ARCHITECTURE_ARM = 5;
    public const ushort PROCESSOR_ARCHITECTURE_IA64 = 6;
    public const ushort PROCESSOR_ARCHITECTURE_AMD64 = 9;
    public const ushort PROCESSOR_ARCHITECTURE_ARM64 = 12;
    public const ushort PROCESSOR_ARCHITECTURE_UNKNOWN = 0xFFFF;

    [DllImport("kernel32.dll")]
    public static extern void GetSystemInfo(out SYSTEM_INFO lpSystemInfo);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    public struct SHQUERYRBINFO
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    /// <summary>
    /// 只读查询回收站：pszRootPath 传盘根目录（如 "C:\"）查询该盘，传 null 查询全部驱动器。
    /// 返回回收站总字节数与项数，不执行任何删除。返回 HRESULT（0 = 成功）。
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);
}
