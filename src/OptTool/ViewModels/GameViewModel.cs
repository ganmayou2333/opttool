using System.Collections.ObjectModel;
using OptTool.Models;
using OptTool.Services;

namespace OptTool.ViewModels;

/// <summary>适配器行。</summary>
public sealed class AdapterRowViewModel : ObservableObject
{
    public required string Description { get; init; }
    public required string KindText { get; init; }
    public required string VendorText { get; init; }
    public required string VramText { get; init; }
    public required bool IsSoftware { get; init; }
}

/// <summary>驱动行。</summary>
public sealed class DriverRowViewModel : ObservableObject
{
    public required string AdapterDescription { get; init; }
    public required string DriverVersion { get; init; }
    public required string DateText { get; init; }
    public required string DaysText { get; init; }
    public required bool Outdated { get; init; }
}

/// <summary>特性等级行。</summary>
public sealed class FeatureRowViewModel : ObservableObject
{
    public required string AdapterDescription { get; init; }
    public required string FeatureLevelText { get; init; }
}

/// <summary>
/// 游戏环境页视图模型。聚合 DXGI 适配器、驱动、D3D12 特性等级与 HAGS 状态。
/// 全部只读展示，不注入游戏进程、不改系统设置（见 ARCHITECTURE.md §6.2）。
/// </summary>
public sealed class GameViewModel : ObservableObject
{
    /// <summary>诚实声明：游戏优化收益有限，瓶颈主要在硬件。</summary>
    public const string HonestStatement =
        "诚实说明：游戏优化实际收益通常只有几个百分点，瓶颈主要在硬件。" +
        "本页只做环境体检，不注入游戏进程、不修改游戏配置。";

    private bool _isLoading = true;
    private string _statusText = "正在采集显卡与驱动信息…";
    private string _hagsText = "不可用";
    private string _driverWarningText = "";
    private bool _hasDriverWarning;

    public ObservableCollection<AdapterRowViewModel> Adapters { get; } = new();
    public ObservableCollection<DriverRowViewModel> Drivers { get; } = new();
    public ObservableCollection<FeatureRowViewModel> Features { get; } = new();

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string HagsText
    {
        get => _hagsText;
        private set => SetProperty(ref _hagsText, value);
    }

    public string DriverWarningText
    {
        get => _driverWarningText;
        private set => SetProperty(ref _driverWarningText, value);
    }

    public bool HasDriverWarning
    {
        get => _hasDriverWarning;
        private set => SetProperty(ref _hasDriverWarning, value);
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = "正在采集显卡与驱动信息…";

        try
        {
            var summary = await Task.Run(() => new GpuInfoService().GetSummary());

            Adapters.Clear();
            foreach (var a in summary.Adapters)
            {
                long vramMb = (long)(a.DedicatedVideoMemory / 1024 / 1024);
                string vendor = GpuInfoService.VendorIdToName(a.VendorId);

                Adapters.Add(new AdapterRowViewModel
                {
                    Description = a.Description,
                    KindText = a.IsSoftware ? "软件渲染" : "硬件 GPU",
                    VendorText = $"{vendor}（0x{a.VendorId:X4}:0x{a.DeviceId:X4}）",
                    VramText = a.IsSoftware ? "无专用显存" : $"{vramMb} MB 专用显存",
                    IsSoftware = a.IsSoftware,
                });
            }

            Drivers.Clear();
            // 同一驱动可能在显示类注册表的多个 000x 子键下重复登记（实测虚拟显示
            // 适配器有 5 条重复），在展示层按 描述+版本+日期 去重（不改服务 API）。
            var seenDrivers = new HashSet<(string, string, DateOnly?)>();
            foreach (var d in summary.Drivers)
            {
                if (!seenDrivers.Add((d.AdapterDescription, d.DriverVersion, d.DriverDate)))
                {
                    continue;
                }

                bool outdated = d.DaysSinceDriverDate is > 365;

                Drivers.Add(new DriverRowViewModel
                {
                    AdapterDescription = d.AdapterDescription,
                    DriverVersion = d.DriverVersion,
                    DateText = d.DriverDate.HasValue ? d.DriverDate.Value.ToString("yyyy-MM-dd") : "不可用",
                    DaysText = d.DaysSinceDriverDate.HasValue
                        ? $"驱动已发布 {d.DaysSinceDriverDate.Value} 天"
                        : "发布日期不可用",
                    Outdated = outdated,
                });
            }

            Features.Clear();
            foreach (var f in summary.Features)
            {
                Features.Add(new FeatureRowViewModel
                {
                    AdapterDescription = f.AdapterDescription,
                    FeatureLevelText = f.SupportsDirectX12
                        ? $"支持 DirectX 12，最高特性等级 {f.HighestFeatureLevel}"
                        : $"不支持 DX12（{f.UnavailableReason}）",
                });
            }

            // HAGS
            if (summary.Scheduling is { } sched)
            {
                HagsText = sched.HardwareAcceleratedGpuSchedulingEnabled switch
                {
                    true => "硬件加速 GPU 调度（HAGS）：已启用",
                    false => "硬件加速 GPU 调度（HAGS）：已禁用",
                    null => $"硬件加速 GPU 调度（HAGS）：读取不可用（{sched.UnavailableReason}）",
                };
            }
            else
            {
                HagsText = "硬件加速 GPU 调度（HAGS）：不可用";
            }

            // 驱动过旧提示
            var outdatedDrivers = Drivers.Where(d => d.Outdated).ToList();
            HasDriverWarning = outdatedDrivers.Count > 0;
            DriverWarningText = HasDriverWarning
                ? string.Join("\n", outdatedDrivers.Select(d =>
                    $"{d.AdapterDescription}：{d.DaysText}，超过 365 天，可考虑到显卡厂商官网更新驱动。"))
                : "";

            StatusText = $"采集完成：{Adapters.Count(a => !a.IsSoftware)} 个硬件适配器、" +
                         $"{Drivers.Count} 条驱动记录。";
        }
        catch (Exception ex)
        {
            StatusText = $"采集失败：{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
