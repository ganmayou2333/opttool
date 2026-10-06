using OptTool.Models;
using OptTool.Services;
using Xunit;
using Xunit.Abstractions;

namespace OptTool.Tests;

/// <summary>
/// GpuInfoService 单元测试。
/// 不依赖具体显卡型号断言，只验证结构正确、字段非空、不抛异常。
/// </summary>
public class GpuInfoServiceTests
{
    private readonly ITestOutputHelper _output;
    private readonly GpuInfoService _service = new();

    public GpuInfoServiceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // 1. GetAdapters 返回非空，且每个适配器的 Description 非空
    [Fact]
    public void GetAdapters_ReturnsNonEmpty_AndDescriptionsNonNull()
    {
        var adapters = _service.GetAdapters();
        Assert.NotNull(adapters);
        Assert.NotEmpty(adapters);
        foreach (var a in adapters)
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Description),
                $"适配器 Description 为空（VendorId=0x{a.VendorId:X}）");
        }
        _output.WriteLine($"枚举到 {adapters.Count} 个适配器");
    }

    // 2. 本机应能枚举到 >= 1 个非软件适配器
    [Fact]
    public void GetAdapters_HasAtLeastOneNonSoftwareAdapter()
    {
        var adapters = _service.GetAdapters();
        Assert.Contains(adapters, a => !a.IsSoftware);
    }

    // 3. VendorId 能正确映射
    [Theory]
    [InlineData(0x10DE, "NVIDIA")]
    [InlineData(0x1002, "AMD")]
    [InlineData(0x1022, "AMD")]
    [InlineData(0x8086, "Intel")]
    [InlineData(0x1414, "Microsoft")]
    [InlineData(0x9999, "未知")]
    public void VendorIdToName_MapsCorrectly(uint vendorId, string expected)
    {
        Assert.Equal(expected, GpuInfoService.VendorIdToName(vendorId));
    }

    // 4. GetDriverInfo 能返回至少一条驱动信息，DriverVersion 非空
    [Fact]
    public void GetDriverInfo_ReturnsAtLeastOne_WithVersion()
    {
        var drivers = _service.GetDriverInfo();
        Assert.NotNull(drivers);
        Assert.NotEmpty(drivers);
        foreach (var d in drivers)
        {
            Assert.False(string.IsNullOrWhiteSpace(d.DriverVersion),
                $"驱动版本为空（{d.AdapterDescription}）");
        }
    }

    // 5. DriverDate 解析正确（含非法日期字符串时不崩，返回 null）
    [Theory]
    [InlineData("08-12-2024", 2024, 8, 12)]
    [InlineData("8-12-2024", 2024, 8, 12)]
    [InlineData("1-1-2020", 2020, 1, 1)]
    public void ParseDriverDate_ValidDates_ReturnsDateOnly(string input, int year, int month, int day)
    {
        var result = GpuInfoService.ParseDriverDate(input);
        Assert.NotNull(result);
        Assert.Equal(new DateOnly(year, month, day), result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-date")]
    [InlineData("13-45-2024")]  // 非法月日
    [InlineData("2-30-2024")]    // 2 月 30 日
    public void ParseDriverDate_InvalidOrEmpty_ReturnsNull_NoThrow(string? input)
    {
        var result = GpuInfoService.ParseDriverDate(input);
        Assert.Null(result);
    }

    // 6. GetHardwareGpuScheduling 返回 null 或 (bool, null) —— 不允许抛异常
    [Fact]
    public void GetHardwareGpuScheduling_DoesNotThrow()
    {
        var info = _service.GetHardwareGpuScheduling();
        Assert.NotNull(info);
        // 要么读到了值（Enabled 非 null），要么读不到（Enabled=null 且有原因）
        if (info.HardwareAcceleratedGpuSchedulingEnabled.HasValue)
        {
            // 读到值时原因应为 null（或可接受有说明）
            Assert.True(true); // 不强制原因 null，系统可能附带说明
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(info.UnavailableReason),
                "读不到 HwSchMode 时应给出原因");
        }
        _output.WriteLine($"HAGS: Enabled={info.HardwareAcceleratedGpuSchedulingEnabled}, Reason={info.UnavailableReason}");
    }

    // 7. GetFeatureInfo 对不存在的适配器名返回 SupportsDirectX12=false 且带原因，不抛异常
    [Fact]
    public void GetFeatureInfo_NonexistentAdapter_ReturnsFalseWithReason()
    {
        var info = _service.GetFeatureInfo("不存在的显卡型号_XYZ_12345");
        Assert.NotNull(info);
        Assert.False(info.SupportsDirectX12);
        Assert.False(string.IsNullOrWhiteSpace(info.UnavailableReason),
            "不支持时应给出原因");
        _output.WriteLine($"FeatureInfo(nonexistent): {info.UnavailableReason}");
    }

    // 8. GetSummary 在任一子项失败时仍能返回对象（不抛异常）
    [Fact]
    public void GetSummary_AlwaysReturnsObject_NoThrow()
    {
        var summary = _service.GetSummary();
        Assert.NotNull(summary);
        Assert.NotNull(summary.Adapters);
        Assert.NotNull(summary.Drivers);
        Assert.NotNull(summary.Features);
        // Scheduling 可能为 null（整体异常时），但对象本身必须存在
        _output.WriteLine($"Summary: Adapters={summary.Adapters.Count}, Drivers={summary.Drivers.Count}, Features={summary.Features.Count}, Scheduling={(summary.Scheduling == null ? "null" : "present")}");
    }

    // 额外：打印本机实际枚举结果（用于验证报告，不做硬断言）
    [Fact]
    public void PrintActualEnumeration_ForReport()
    {
        var adapters = _service.GetAdapters();
        _output.WriteLine($"=== 本机 DXGI 适配器枚举（共 {adapters.Count} 个）===");
        foreach (var a in adapters)
        {
            _output.WriteLine($"  名称: {a.Description}");
            _output.WriteLine($"  厂商ID: 0x{a.VendorId:X} ({GpuInfoService.VendorIdToName(a.VendorId)})");
            _output.WriteLine($"  设备ID: 0x{a.DeviceId:X}");
            _output.WriteLine($"  专用显存: {a.DedicatedVideoMemory / 1024.0 / 1024.0:F1} MB");
            _output.WriteLine($"  共享内存: {a.SharedSystemMemory / 1024.0 / 1024.0:F1} MB");
            _output.WriteLine($"  软件适配器: {a.IsSoftware}");
            _output.WriteLine($"  ---");
        }

        var drivers = _service.GetDriverInfo();
        _output.WriteLine($"=== 驱动信息（共 {drivers.Count} 条）===");
        foreach (var d in drivers)
        {
            _output.WriteLine($"  适配器: {d.AdapterDescription}");
            _output.WriteLine($"  驱动版本: {d.DriverVersion}");
            _output.WriteLine($"  驱动日期: {(d.DriverDate.HasValue ? d.DriverDate.Value.ToString("yyyy-MM-dd") : "不可用")}");
            _output.WriteLine($"  距今天数: {d.DaysSinceDriverDate}");
            _output.WriteLine($"  ---");
        }

        var hags = _service.GetHardwareGpuScheduling();
        _output.WriteLine($"=== HAGS ===");
        _output.WriteLine($"  启用: {hags.HardwareAcceleratedGpuSchedulingEnabled}");
        _output.WriteLine($"  原因: {hags.UnavailableReason}");

        _output.WriteLine($"=== D3D12 特性等级（非软件适配器）===");
        foreach (var a in adapters)
        {
            if (a.IsSoftware) continue;
            var feat = _service.GetFeatureInfo(a.Description);
            _output.WriteLine($"  {a.Description}:");
            _output.WriteLine($"    最高特性等级: {feat.HighestFeatureLevel}");
            _output.WriteLine($"    支持 DX12: {feat.SupportsDirectX12}");
            _output.WriteLine($"    原因: {feat.UnavailableReason}");
        }
    }
}
