namespace OptTool.Models;

/// <summary>CPU 信息。</summary>
public record CpuInfo(
    string Architecture,
    int LogicalProcessorCount,
    int PageSize,
    string Model,
    int NominalFrequencyMHz);

/// <summary>物理内存信息。</summary>
public record MemoryInfo(
    long TotalPhysicalBytes,
    long AvailablePhysicalBytes,
    double UsagePercent);

/// <summary>操作系统信息。</summary>
public record OsInfo(
    string ProductName,
    string Version,
    string Build,
    string UBR,
    string EditionID);

/// <summary>磁盘卷信息。</summary>
public record DiskInfo(
    string DriveLetter,
    string VolumeLabel,
    long TotalSizeBytes,
    long AvailableSizeBytes);

/// <summary>启动项条目。</summary>
public record StartupEntry(
    string Name,
    string Command,
    string Source);

/// <summary>提权状态。</summary>
public record ElevationStatus(
    bool IsElevated,
    bool CanWriteSystemSettings,
    string Reason);
