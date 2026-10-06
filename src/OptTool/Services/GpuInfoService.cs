using Microsoft.Win32;
using OptTool.Interop;
using OptTool.Models;

namespace OptTool.Services;

/// <summary>
/// DirectX / 显卡体检服务。
/// 用 DXGI 枚举全部适配器（不只用第一个），用注册表取驱动信息，
/// 用 D3D12CreateDevice 探测最高特性等级。
/// 所有系统调用包 try/catch，单项失败返回"不可用"或空集合，不抛异常。
/// 禁止 WMI / CIM / 外部 shell 命令。
/// </summary>
public class GpuInfoService
{
    private const string DisplayClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    private const string GraphicsDriversKey =
        @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

    // ---- 厂商 ID 映射（公开，供单元测试） ----

    /// <summary>将 PCI 厂商 ID 映射为厂商名称。未知厂商返回 "未知"。</summary>
    public static string VendorIdToName(uint vendorId)
    {
        return vendorId switch
        {
            0x10DE => "NVIDIA",
            0x1002 => "AMD",
            0x1022 => "AMD",
            0x8086 => "Intel",
            0x1414 => "Microsoft",
            _ => "未知",
        };
    }

    // ---- 驱动日期解析（公开，供单元测试） ----

    /// <summary>
    /// 解析注册表中的 DriverDate 字符串（常见格式 MM-DD-YYYY，也可能带前导零）。
    /// 解析失败返回 null，不抛异常。
    /// </summary>
    public static DateOnly? ParseDriverDate(string? dateText)
    {
        if (string.IsNullOrWhiteSpace(dateText)) return null;

        try
        {
            // 优先按 MM-DD-YYYY 手动解析（注册表最常见格式）
            var parts = dateText.Trim().Split('-', '/', '.');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], out int month) &&
                int.TryParse(parts[1], out int day) &&
                int.TryParse(parts[2], out int year))
            {
                if (year < 100) year += 2000; // 两位年份补全
                if (month is >= 1 and <= 12 && day is >= 1 and <= 31 &&
                    year is >= 1980 and <= 2100)
                {
                    try { return new DateOnly(year, month, day); }
                    catch { /* 非法日期（如 2 月 30 日）继续尝试通用解析 */ }
                }
            }

            // 退回通用解析
            if (DateOnly.TryParse(dateText, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }
        }
        catch
        {
            // 任何异常返回 null
        }

        return null;
    }

    // ---- 公开 API ----

    /// <summary>枚举全部 DXGI 适配器（含软件适配器，已按 LUID 去重）。失败返回空列表。</summary>
    public IReadOnlyList<GpuAdapterInfo> GetAdapters()
    {
        try
        {
            var descs = DxgiEnumerator.EnumerateAdapters();
            var list = new List<GpuAdapterInfo>(descs.Count);
            // 同一块物理显卡在 DXGI 里可能出现多次（混合显卡 / 远程会话 / 不同 LUID 的重复注册），
            // 实测 RTX 5070 会以两个不同 LUID 出现两次。
            // LUID 不足以去重，因此用「厂商ID + 设备ID + 专用显存」作为身份键。
            var seen = new HashSet<(uint VendorId, uint DeviceId, ulong Memory)>();

            foreach (var d in descs)
            {
                var key = (d.VendorId, d.DeviceId, (ulong)d.DedicatedVideoMemory);
                if (!seen.Add(key))
                {
                    continue; // 同一块显卡的重复条目
                }

                string name = string.IsNullOrWhiteSpace(d.Description) ? "未知适配器" : d.Description.Trim();

                list.Add(new GpuAdapterInfo(
                    Description: name,
                    VendorId: d.VendorId,
                    DeviceId: d.DeviceId,
                    DedicatedVideoMemory: (ulong)d.DedicatedVideoMemory,
                    SharedSystemMemory: (ulong)d.SharedSystemMemory,
                    IsSoftware: IsSoftwareAdapter(d.Flags, d.VendorId, name),
                    AdapterLuid: d.AdapterLuid));
            }
            return list;
        }
        catch
        {
            return Array.Empty<GpuAdapterInfo>();
        }
    }

    /// <summary>DXGI_ADAPTER_FLAG_SOFTWARE = 2（bit1）。</summary>
    private const uint DxgiAdapterFlagSoftware = 2;

    /// <summary>Microsoft 基本渲染驱动（WARP）的厂商 ID。</summary>
    private const uint VendorIdMicrosoft = 0x1414;

    /// <summary>
    /// 判断是否为软件适配器。
    /// 注意：WARP（Microsoft Basic Render Driver）通常并不设置 DXGI_ADAPTER_FLAG_SOFTWARE，
    /// 因此必须同时按厂商 ID 与名称兜底判定，否则界面上会把它当成一块真实显卡。
    /// </summary>
    private static bool IsSoftwareAdapter(uint flags, uint vendorId, string description)
    {
        if ((flags & DxgiAdapterFlagSoftware) != 0)
        {
            return true;
        }

        if (vendorId == VendorIdMicrosoft)
        {
            return true;
        }

        return description.Contains("Basic Render", StringComparison.OrdinalIgnoreCase)
            || description.Contains("WARP", StringComparison.OrdinalIgnoreCase)
            || description.Contains("Remote Display", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 从注册表显示类键读取全部显卡驱动信息。
    /// 失败返回空列表。
    /// </summary>
    public IReadOnlyList<GpuDriverInfo> GetDriverInfo()
    {
        var results = new List<GpuDriverInfo>();

        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (classKey == null) return results;

            foreach (var subName in classKey.GetSubKeyNames())
            {
                // 只处理 0000, 0001, ... 这种数字子键
                if (!int.TryParse(subName, out _)) continue;

                try
                {
                    using var subKey = classKey.OpenSubKey(subName);
                    if (subKey == null) continue;

                    string? desc = subKey.GetValue("DriverDesc") as string;
                    string? version = subKey.GetValue("DriverVersion") as string;
                    string? dateText = subKey.GetValue("DriverDate") as string;

                    if (string.IsNullOrWhiteSpace(desc) && string.IsNullOrWhiteSpace(version))
                        continue;

                    DateOnly? driverDate = ParseDriverDate(dateText);
                    int? daysSince = null;
                    if (driverDate.HasValue)
                    {
                        try
                        {
                            var today = DateOnly.FromDateTime(DateTime.Today);
                            daysSince = today.DayNumber - driverDate.Value.DayNumber;
                        }
                        catch { /* 忽略 */ }
                    }

                    results.Add(new GpuDriverInfo(
                        AdapterDescription: desc ?? "未知适配器",
                        DriverVersion: version ?? "不可用",
                        DriverDate: driverDate,
                        DaysSinceDriverDate: daysSince));
                }
                catch
                {
                    // 单个子键失败跳过
                }
            }
        }
        catch
        {
            // 整体失败返回已收集部分
        }

        return results;
    }

    /// <summary>
    /// 对指定适配器名探测 D3D12 最高特性等级。
    /// 适配器不存在或探测失败时返回 SupportsDirectX12=false 并带原因，不抛异常。
    /// </summary>
    public GpuFeatureInfo GetFeatureInfo(string adapterDescription)
    {
        if (string.IsNullOrWhiteSpace(adapterDescription))
        {
            return new GpuFeatureInfo(
                AdapterDescription: adapterDescription ?? string.Empty,
                HighestFeatureLevel: "不可用",
                SupportsDirectX12: false,
                UnavailableReason: "适配器名称为空");
        }

        nint adapterPtr = nint.Zero;
        try
        {
            adapterPtr = DxgiEnumerator.GetAdapterIUnknownByName(adapterDescription);
            if (adapterPtr == nint.Zero)
            {
                return new GpuFeatureInfo(
                    AdapterDescription: adapterDescription,
                    HighestFeatureLevel: "不可用",
                    SupportsDirectX12: false,
                    UnavailableReason: $"未找到名为 \"{adapterDescription}\" 的 DXGI 适配器");
            }

            string? level = D3D12Interop.ProbeHighestFeatureLevel(adapterPtr);
            if (string.IsNullOrEmpty(level))
            {
                return new GpuFeatureInfo(
                    AdapterDescription: adapterDescription,
                    HighestFeatureLevel: "不可用",
                    SupportsDirectX12: false,
                    UnavailableReason: "D3D12 设备创建失败，该适配器可能不支持 DirectX 12");
            }

            return new GpuFeatureInfo(
                AdapterDescription: adapterDescription,
                HighestFeatureLevel: level,
                SupportsDirectX12: true,
                UnavailableReason: null);
        }
        catch (Exception ex)
        {
            return new GpuFeatureInfo(
                AdapterDescription: adapterDescription,
                HighestFeatureLevel: "不可用",
                SupportsDirectX12: false,
                UnavailableReason: $"探测异常: {ex.Message}");
        }
        finally
        {
            if (adapterPtr != nint.Zero)
            {
                try { Marshal_Release(adapterPtr); } catch { }
            }
        }
    }

    // 用独立方法避免在 catch 块中直接 using 别名问题
    private static void Marshal_Release(nint ptr) => System.Runtime.InteropServices.Marshal.Release(ptr);

    /// <summary>
    /// 读取硬件加速 GPU 调度（HAGS）状态。
    /// 注册表 HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers 的 HwSchMode 值：
    /// 2 = 启用，1 = 禁用。读不到返回 null + 原因。
    /// </summary>
    public HardwareGpuSchedulingInfo GetHardwareGpuScheduling()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(GraphicsDriversKey);
            if (key == null)
            {
                return new HardwareGpuSchedulingInfo(
                    HardwareAcceleratedGpuSchedulingEnabled: null,
                    UnavailableReason: "注册表项 GraphicsDrivers 不存在");
            }

            var value = key.GetValue("HwSchMode");
            if (value == null)
            {
                return new HardwareGpuSchedulingInfo(
                    HardwareAcceleratedGpuSchedulingEnabled: null,
                    UnavailableReason: "HwSchMode 值未设置（系统默认，通常为禁用）");
            }

            int mode = Convert.ToInt32(value);
            return mode switch
            {
                2 => new HardwareGpuSchedulingInfo(true, null),
                1 => new HardwareGpuSchedulingInfo(false, null),
                _ => new HardwareGpuSchedulingInfo(null, $"HwSchMode 为未知值 {mode}"),
            };
        }
        catch (Exception ex)
        {
            return new HardwareGpuSchedulingInfo(
                HardwareAcceleratedGpuSchedulingEnabled: null,
                UnavailableReason: $"读取异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 聚合显卡总览。任一子项失败仍返回对象（对应字段为空集合或 null），不抛异常。
    /// </summary>
    public GameEnvironmentGpuSummary GetSummary()
    {
        IReadOnlyList<GpuAdapterInfo> adapters;
        IReadOnlyList<GpuDriverInfo> drivers;
        IReadOnlyList<GpuFeatureInfo> features;
        HardwareGpuSchedulingInfo? scheduling;

        try { adapters = GetAdapters(); }
        catch { adapters = Array.Empty<GpuAdapterInfo>(); }

        try { drivers = GetDriverInfo(); }
        catch { drivers = Array.Empty<GpuDriverInfo>(); }

        // 对每个非软件适配器探测特性等级
        var featureList = new List<GpuFeatureInfo>();
        try
        {
            foreach (var adapter in adapters)
            {
                if (adapter.IsSoftware) continue;
                try
                {
                    featureList.Add(GetFeatureInfo(adapter.Description));
                }
                catch { /* 单个适配器探测失败跳过 */ }
            }
        }
        catch { /* 整体失败保留已收集部分 */ }
        features = featureList;

        try { scheduling = GetHardwareGpuScheduling(); }
        catch { scheduling = null; }

        return new GameEnvironmentGpuSummary(
            Adapters: adapters,
            Drivers: drivers,
            Features: features,
            Scheduling: scheduling);
    }
}
