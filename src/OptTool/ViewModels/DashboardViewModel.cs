using System.ComponentModel;
using System.Runtime.CompilerServices;
using OptTool.Models;
using OptTool.Services;

namespace OptTool.ViewModels;

/// <summary>
/// 体检仪表盘视图模型。所有采集在后台线程执行，属性变更通过 INotifyPropertyChanged 通知界面。
/// 单项失败一律降级为"不可用"，不向界面抛异常。
/// </summary>
public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private readonly SystemInfoService _sysInfo = new();
    private readonly GpuInfoService _gpuInfo = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(T value, [CallerMemberName] string? name = null)
    {
        if (PropertyChanged is null || name is null) return;
        PropertyChanged(this, new PropertyChangedEventArgs(name));
        _ = value;
    }

    // ---- 状态卡 1：系统 ----
    private string _osName = "加载中…";
    public string OsName { get => _osName; private set { _osName = value; Set(value); } }

    private string _cpuName = "加载中…";
    public string CpuName { get => _cpuName; private set { _cpuName = value; Set(value); } }

    // ---- 状态卡 2：内存 ----
    private string _memoryUsed = "加载中…";
    public string MemoryUsed { get => _memoryUsed; private set { _memoryUsed = value; Set(value); } }

    private double _memoryPercent;
    public double MemoryPercent { get => _memoryPercent; private set { _memoryPercent = value; Set(value); } }

    private string _memoryDetail = "";
    public string MemoryDetail { get => _memoryDetail; private set { _memoryDetail = value; Set(value); } }

    // ---- 状态卡 3：显卡 ----
    private string _gpuName = "加载中…";
    public string GpuName { get => _gpuName; private set { _gpuName = value; Set(value); } }

    private string _gpuDetail = "";
    public string GpuDetail { get => _gpuDetail; private set { _gpuDetail = value; Set(value); } }

    // ---- 状态卡 4：DirectX ----
    private string _featureLevel = "加载中…";
    public string FeatureLevel { get => _featureLevel; private set { _featureLevel = value; Set(value); } }

    private string _dxDetail = "";
    public string DxDetail { get => _dxDetail; private set { _dxDetail = value; Set(value); } }

    // ---- 明细 ----
    private string _details = "";
    public string Details { get => _details; private set { _details = value; Set(value); } }

    private string _status = "正在采集系统信息…";
    public string Status { get => _status; private set { _status = value; Set(value); } }

    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; private set { _isLoading = value; Set(value); } }

    public async Task LoadAsync()
    {
        IsLoading = true;
        Status = "正在采集系统信息…";

        try
        {
            await Task.Run(() =>
            {
                // ---- CPU / OS ----
                try
                {
                    var cpu = _sysInfo.GetCpu();
                    CpuName = string.IsNullOrWhiteSpace(cpu.Model) ? "未知处理器" : cpu.Model;
                }
                catch { CpuName = "不可用"; }

                try
                {
                    var os = _sysInfo.GetOs();
                    OsName = os.ProductName;
                }
                catch { OsName = "不可用"; }

                // ---- 内存 ----
                try
                {
                    var mem = _sysInfo.GetMemory();
                    double totalGb = mem.TotalPhysicalBytes / 1024.0 / 1024 / 1024;
                    double availGb = mem.AvailablePhysicalBytes / 1024.0 / 1024 / 1024;
                    MemoryPercent = mem.UsagePercent;
                    MemoryUsed = $"{mem.UsagePercent}%";
                    MemoryDetail = $"已用 {totalGb - availGb:F1} GB / 共 {totalGb:F1} GB";
                }
                catch { MemoryUsed = "不可用"; }

                // ---- 磁盘 ----
                string diskLine = "不可用";
                try
                {
                    var disks = _sysInfo.GetDisks();
                    if (disks.Count > 0)
                    {
                        var d = disks[0];
                        double totalGb = d.TotalSizeBytes / 1024.0 / 1024 / 1024;
                        double freeGb = d.AvailableSizeBytes / 1024.0 / 1024 / 1024;
                        diskLine = $"{d.DriveLetter} 可用 {freeGb:F0} GB / 共 {totalGb:F0} GB";
                    }
                }
                catch { }

                // ---- 显卡 ----
                IReadOnlyList<GpuAdapterInfo> adapters = Array.Empty<GpuAdapterInfo>();
                try
                {
                    adapters = _gpuInfo.GetAdapters();
                    var hardware = adapters.Where(a => !a.IsSoftware).ToList();
                    int count = hardware.Count;

                    if (count > 0)
                    {
                        var best = hardware.OrderByDescending(a => a.DedicatedVideoMemory).First();
                        GpuName = best.Description;
                        double vramMb = best.DedicatedVideoMemory / 1024.0 / 1024;
                        GpuDetail = count > 1
                            ? $"{count} 个硬件适配器 · 最高 {vramMb:F0} MB 显存"
                            : $"{vramMb:F0} MB 显存";
                    }
                    else
                    {
                        GpuName = "未检测到硬件显卡";
                        GpuDetail = "仅软件渲染适配器";
                    }
                }
                catch { GpuName = "不可用"; }

                // ---- DirectX 特性等级 + HAGS ----
                string hagsLine = "不可用";
                try
                {
                    var hags = _gpuInfo.GetHardwareGpuScheduling();
                    hagsLine = hags.HardwareAcceleratedGpuSchedulingEnabled switch
                    {
                        true => "硬件加速 GPU 调度：已启用",
                        false => "硬件加速 GPU 调度：已禁用",
                        null => "硬件加速 GPU 调度：读取不可用"
                    };
                }
                catch { }

                var hardwareAdapters = adapters.Where(a => !a.IsSoftware).ToList();
                if (hardwareAdapters.Count > 0)
                {
                    try
                    {
                        var f = _gpuInfo.GetFeatureInfo(hardwareAdapters[0].Description);
                        FeatureLevel = f.SupportsDirectX12 ? f.HighestFeatureLevel : "不支持 DX12";
                        DxDetail = hagsLine;
                    }
                    catch
                    {
                        FeatureLevel = "不可用";
                        DxDetail = hagsLine;
                    }
                }
                else
                {
                    FeatureLevel = "无硬件显卡";
                    DxDetail = hagsLine;
                }

                // ---- 明细区 ----
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"系统      ：{OsName}");
                sb.AppendLine($"处理器    ：{CpuName}");
                sb.AppendLine($"内存      ：{MemoryDetail}（占用 {MemoryPercent}%）");
                sb.AppendLine($"磁盘      ：{diskLine}");
                sb.AppendLine($"显卡      ：{GpuName} — {GpuDetail}");
                foreach (var a in adapters)
                {
                    string kind = a.IsSoftware ? "软件" : "硬件";
                    sb.AppendLine($"            · [{kind}] {a.Description}  0x{a.VendorId:X4}:0x{a.DeviceId:X4}  {a.DedicatedVideoMemory / 1024 / 1024} MB");
                }
                sb.AppendLine($"DirectX   ：特性等级 {FeatureLevel}");
                sb.AppendLine($"            {hagsLine}");
                Details = sb.ToString().TrimEnd();
            });

            Status = "采集完成";
        }
        catch (Exception ex)
        {
            Status = $"采集失败：{ex.GetType().Name}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
