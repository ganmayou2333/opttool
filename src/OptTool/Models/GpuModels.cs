namespace OptTool.Models;

/// <summary>单个显卡适配器信息（来自 DXGI 枚举）。</summary>
public record GpuAdapterInfo(
    string Description,
    uint VendorId,
    uint DeviceId,
    ulong DedicatedVideoMemory,
    ulong SharedSystemMemory,
    bool IsSoftware,
    long AdapterLuid = 0);

/// <summary>显卡驱动信息（来自注册表）。</summary>
public record GpuDriverInfo(
    string AdapterDescription,
    string DriverVersion,
    DateOnly? DriverDate,
    int? DaysSinceDriverDate);

/// <summary>D3D12 特性等级支持情况。</summary>
public record GpuFeatureInfo(
    string AdapterDescription,
    string HighestFeatureLevel,
    bool SupportsDirectX12,
    string? UnavailableReason);

/// <summary>硬件加速 GPU 调度（HAGS）状态。</summary>
public record HardwareGpuSchedulingInfo(
    bool? HardwareAcceleratedGpuSchedulingEnabled,
    string? UnavailableReason);

/// <summary>游戏环境显卡总览，聚合适配器 / 驱动 / 特性 / HAGS。</summary>
public record GameEnvironmentGpuSummary(
    IReadOnlyList<GpuAdapterInfo> Adapters,
    IReadOnlyList<GpuDriverInfo> Drivers,
    IReadOnlyList<GpuFeatureInfo> Features,
    HardwareGpuSchedulingInfo? Scheduling);
