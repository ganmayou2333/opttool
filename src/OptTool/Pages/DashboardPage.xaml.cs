using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OptTool.Models;
using OptTool.Services;

namespace OptTool.Pages;

/// <summary>
/// 体检仪表盘。
/// 重要：属性直接定义在 Page 上并实现 INotifyPropertyChanged。
/// x:Bind 使用的通知键是绑定路径本身（这里是属性名），此前把属性放在
/// ViewModel 上绑成 {x:Bind ViewModel.X}，ViewModel 发出的通知键是 "X"，
/// 与绑定监听的 "ViewModel.X" 不匹配，导致界面永远停在"加载中"。
/// </summary>
public sealed partial class DashboardPage : Page, INotifyPropertyChanged
{
    private readonly SystemInfoService _sysInfo = new();
    private readonly GpuInfoService _gpuInfo = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(T value, [CallerMemberName] string? name = null)
    {
        if (name is null) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public DashboardPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // ---- 状态卡属性 ----
    private string _osName = "加载中…";
    public string OsName { get => _osName; private set { _osName = value; Set(value); } }

    private string _cpuName = "加载中…";
    public string CpuName { get => _cpuName; private set { _cpuName = value; Set(value); } }

    private string _memoryUsed = "加载中…";
    public string MemoryUsed { get => _memoryUsed; private set { _memoryUsed = value; Set(value); } }

    private string _memoryDetail = "";
    public string MemoryDetail { get => _memoryDetail; private set { _memoryDetail = value; Set(value); } }

    private string _gpuName = "加载中…";
    public string GpuName { get => _gpuName; private set { _gpuName = value; Set(value); } }

    private string _gpuDetail = "";
    public string GpuDetail { get => _gpuDetail; private set { _gpuDetail = value; Set(value); } }

    private string _featureLevel = "加载中…";
    public string FeatureLevel { get => _featureLevel; private set { _featureLevel = value; Set(value); } }

    private string _dxDetail = "";
    public string DxDetail { get => _dxDetail; private set { _dxDetail = value; Set(value); } }

    private string _details = "";
    public string Details { get => _details; private set { _details = value; Set(value); } }

    private bool _loadedOnce;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce) return;   // 来回切页不重复扫描
        _loadedOnce = true;
        await RunScanAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RunScanAsync();
    }

    private async Task RunScanAsync()
    {
        RefreshButton.IsEnabled = false;
        OptimizeButton.IsEnabled = false;
        LoadingBar.Visibility = Visibility.Visible;
        StatusBar.Severity = InfoBarSeverity.Informational;
        StatusBar.Title = "检测状态";
        StatusBar.Message = "正在采集系统信息…";

        try
        {
            // 采集在后台线程执行；但属性变更必须回到 UI 线程触发，
            // 否则 WinUI 绑定在后台线程更新会抛 COMException。
            var dispatcher = DispatcherQueue;
            await Task.Run(() =>
            {
                void Post(Action act) => dispatcher.TryEnqueue(() => act());
                Collect(Post);
            });

            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Title = "检测完成";
            StatusBar.Message = "以上为只读检测结果，未对系统做任何修改。";
        }
        catch (Exception ex)
        {
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Title = "检测失败";
            StatusBar.Message = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            LoadingBar.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = true;
            OptimizeButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 全部采集在后台线程执行；单项失败降级为"不可用"，不抛异常到界面。
    /// post 用于把属性赋值切回 UI 线程（WinUI 绑定要求）。
    /// </summary>
    private void Collect(Action<Action> post)
    {
        // 采集阶段只写局部变量；最后一次性 post 回 UI 线程提交，避免中途半成品状态。
        string cpuText;
        try
        {
            var cpu = _sysInfo.GetCpu();
            cpuText = string.IsNullOrWhiteSpace(cpu.Model) ? "未知处理器" : cpu.Model;
        }
        catch { cpuText = "不可用"; }

        string osText;
        try
        {
            var os = _sysInfo.GetOs();
            osText = string.IsNullOrWhiteSpace(os.ProductName) ? "不可用" : os.ProductName;
        }
        catch { osText = "不可用"; }

        string memoryUsedText;
        string memoryDetailText;
        try
        {
            var mem = _sysInfo.GetMemory();
            double totalGb = mem.TotalPhysicalBytes / 1024.0 / 1024 / 1024;
            double availGb = mem.AvailablePhysicalBytes / 1024.0 / 1024 / 1024;
            memoryUsedText = $"{mem.UsagePercent:0}%";
            memoryDetailText = $"已用 {totalGb - availGb:F1} GB / 共 {totalGb:F1} GB";
        }
        catch { memoryUsedText = "不可用"; memoryDetailText = ""; }

        string diskLine = "不可用";
        try
        {
            var disks = _sysInfo.GetDisks();
            if (disks.Count > 0)
            {
                var d = disks[0];
                diskLine = $"{d.DriveLetter} 可用 {d.AvailableSizeBytes / 1024.0 / 1024 / 1024:F0} GB / 共 {d.TotalSizeBytes / 1024.0 / 1024 / 1024:F0} GB";
            }
        }
        catch { }

        IReadOnlyList<GpuAdapterInfo> adapters = Array.Empty<GpuAdapterInfo>();
        string gpuNameText = "不可用";
        string gpuDetailText = "";
        try
        {
            adapters = _gpuInfo.GetAdapters();
            var hardware = adapters.Where(a => !a.IsSoftware).ToList();
            if (hardware.Count > 0)
            {
                var best = hardware.OrderByDescending(a => a.DedicatedVideoMemory).First();
                double vramMb = best.DedicatedVideoMemory / 1024.0 / 1024;
                gpuNameText = best.Description;
                gpuDetailText = hardware.Count > 1
                    ? $"{hardware.Count} 个硬件适配器 · 最高 {vramMb:F0} MB 显存"
                    : $"{vramMb:F0} MB 显存";
            }
            else
            {
                gpuNameText = "未检测到硬件显卡";
                gpuDetailText = "仅软件渲染适配器";
            }
        }
        catch { }

        string hagsLine = "硬件加速 GPU 调度：读取不可用";
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

        var hwAdapters = adapters.Where(a => !a.IsSoftware).ToList();
        string featureText;
        if (hwAdapters.Count > 0)
        {
            try
            {
                var f = _gpuInfo.GetFeatureInfo(hwAdapters[0].Description);
                featureText = f.SupportsDirectX12 ? f.HighestFeatureLevel : "不支持 DX12";
            }
            catch { featureText = "不可用"; }
        }
        else
        {
            featureText = "无硬件显卡";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"系统      ：{osText}");
        sb.AppendLine($"处理器    ：{cpuText}");
        sb.AppendLine($"内存      ：{memoryDetailText}（占用 {memoryUsedText}）");
        sb.AppendLine($"磁盘      ：{diskLine}");
        sb.AppendLine($"显卡      ：{gpuNameText}");
        foreach (var a in adapters)
        {
            sb.AppendLine($"            · [{(a.IsSoftware ? "软件" : "硬件")}] {a.Description}  0x{a.VendorId:X4}:0x{a.DeviceId:X4}  {a.DedicatedVideoMemory / 1024 / 1024} MB");
        }
        sb.AppendLine($"DirectX   ：{featureText}");
        sb.AppendLine($"            {hagsLine}");
        string detailsText = sb.ToString().TrimEnd();

        // 一次性提交到 UI 线程
        post(() =>
        {
            CpuName = cpuText;
            OsName = osText;
            MemoryUsed = memoryUsedText;
            MemoryDetail = memoryDetailText;
            GpuName = gpuNameText;
            GpuDetail = gpuDetailText;
            FeatureLevel = featureText;
            DxDetail = hagsLine;
            Details = detailsText;
        });
    }

    private async void OptimizeButton_Click(object sender, RoutedEventArgs e)
    {
        ElevationStatus elevation;
        try { elevation = _sysInfo.GetElevation(); }
        catch { elevation = new ElevationStatus(false, false, "提权状态检测失败"); }

        string message = elevation.IsElevated
            ? "一键优化尚未接入（第一版 demo 仅提供只读体检）。\n\n" +
              "后续版本将执行低风险清理项，并保证每一步都可回滚。"
            : "当前未以管理员身份运行，无法修改系统设置。\n\n" +
              "只读体检功能不受影响；需要执行优化时，请右键以管理员身份重新启动。\n\n" +
              $"原因：{elevation.Reason}";

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "一键优化",
            Content = message,
            CloseButtonText = "知道了"
        };

        await dialog.ShowAsync();
    }
}
