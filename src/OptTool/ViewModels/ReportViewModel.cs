using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using OptTool.Services;

namespace OptTool.ViewModels;

/// <summary>报告中的磁盘行。</summary>
public record ReportDisk(string Drive, string Label, long TotalBytes, long FreeBytes);

/// <summary>报告中的适配器行。</summary>
public record ReportAdapter(string Description, bool IsSoftware, ulong DedicatedVideoBytes, uint VendorId, uint DeviceId);

/// <summary>体检报告数据。</summary>
public record CheckupReport(
    string AppName,
    string GeneratedAt,
    string Os,
    string OsVersion,
    string OsBuild,
    string Cpu,
    int LogicalProcessors,
    long MemoryTotalBytes,
    long MemoryAvailableBytes,
    double MemoryUsagePercent,
    IReadOnlyList<ReportDisk> Disks,
    IReadOnlyList<ReportAdapter> Adapters,
    string FeatureLevels,
    string Hags,
    string Elevation);

/// <summary>
/// 报告页视图模型。汇总体检结果并导出 HTML / TXT / JSON 到 data\reports。
/// 目录一律用 AppContext.BaseDirectory 定位（禁止用程序集路径）。
/// </summary>
public sealed class ReportViewModel : ObservableObject
{
    private bool _isLoading = true;
    private string _statusText = "正在汇总体检结果…";
    private string _previewText = "";
    private string _lastExportPath = "尚未导出";
    private bool _hasReport;

    private CheckupReport? _report;

    public string ReportsDir => Path.Combine(AppContext.BaseDirectory, "data", "reports");

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

    public string PreviewText
    {
        get => _previewText;
        private set => SetProperty(ref _previewText, value);
    }

    public string LastExportPath
    {
        get => _lastExportPath;
        private set => SetProperty(ref _lastExportPath, value);
    }

    public bool HasReport
    {
        get => _hasReport;
        private set => SetProperty(ref _hasReport, value);
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = "正在汇总体检结果…";

        try
        {
            _report = await Task.Run(BuildReport);
            PreviewText = BuildTxt(_report);
            HasReport = true;
            StatusText = "汇总完成，可导出 HTML / TXT / JSON。";
        }
        catch (Exception ex)
        {
            StatusText = $"汇总失败：{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static CheckupReport BuildReport()
    {
        var sys = new SystemInfoService();
        var gpu = new GpuInfoService();

        string osText = "不可用";
        string osVersion = "不可用";
        string osBuild = "不可用";
        try
        {
            var os = sys.GetOs();
            osText = os.ProductName;
            osVersion = string.IsNullOrWhiteSpace(os.Version) ? "不可用" : os.Version;
            osBuild = string.IsNullOrWhiteSpace(os.Build)
                ? "不可用"
                : $"{os.Build}.{os.UBR}（{os.EditionID}）";
        }
        catch { }

        string cpuText = "不可用";
        int logical = 0;
        try
        {
            var cpu = sys.GetCpu();
            cpuText = $"{cpu.Model}（{cpu.Architecture}，标称 {cpu.NominalFrequencyMHz} MHz）";
            logical = cpu.LogicalProcessorCount;
        }
        catch { }

        long memTotal = 0, memAvail = 0;
        double memPercent = 0;
        try
        {
            var mem = sys.GetMemory();
            memTotal = mem.TotalPhysicalBytes;
            memAvail = mem.AvailablePhysicalBytes;
            memPercent = mem.UsagePercent;
        }
        catch { }

        var disks = new List<ReportDisk>();
        try
        {
            foreach (var d in sys.GetDisks())
            {
                disks.Add(new ReportDisk(d.DriveLetter, d.VolumeLabel, d.TotalSizeBytes, d.AvailableSizeBytes));
            }
        }
        catch { }

        var adapters = new List<ReportAdapter>();
        try
        {
            foreach (var a in gpu.GetAdapters())
            {
                adapters.Add(new ReportAdapter(
                    a.Description, a.IsSoftware, a.DedicatedVideoMemory, a.VendorId, a.DeviceId));
            }
        }
        catch { }

        // 特性等级：逐个硬件适配器探测。
        var featureLines = new List<string>();
        foreach (var a in adapters.Where(a => !a.IsSoftware))
        {
            try
            {
                var f = gpu.GetFeatureInfo(a.Description);
                featureLines.Add(f.SupportsDirectX12
                    ? $"· {a.Description}：{f.HighestFeatureLevel}"
                    : $"· {a.Description}：不支持 DX12");
            }
            catch
            {
                featureLines.Add($"· {a.Description}：探测不可用");
            }
        }

        string hagsText = "不可用";
        try
        {
            var h = gpu.GetHardwareGpuScheduling();
            hagsText = h.HardwareAcceleratedGpuSchedulingEnabled switch
            {
                true => "已启用",
                false => "已禁用",
                null => $"读取不可用（{h.UnavailableReason}）",
            };
        }
        catch { }

        string elevationText = "不可用";
        try
        {
            var e = sys.GetElevation();
            elevationText = e.IsElevated ? "已提权（管理员）" : $"普通权限（{e.Reason}）";
        }
        catch { }

        return new CheckupReport(
            AppName: "OptTool — 系统优化工具",
            GeneratedAt: DateTimeOffset.Now.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz"),
            Os: osText,
            OsVersion: osVersion,
            OsBuild: osBuild,
            Cpu: cpuText,
            LogicalProcessors: logical,
            MemoryTotalBytes: memTotal,
            MemoryAvailableBytes: memAvail,
            MemoryUsagePercent: memPercent,
            Disks: disks,
            Adapters: adapters,
            FeatureLevels: featureLines.Count > 0
                ? Environment.NewLine + string.Join(Environment.NewLine, featureLines)
                : "无硬件适配器",
            Hags: hagsText,
            Elevation: elevationText);
    }

    /// <summary>导出指定格式，返回写入的文件路径。</summary>
    public async Task<string> ExportAsync(string format)
    {
        if (_report is null)
        {
            await LoadAsync();
        }

        if (_report is null)
        {
            throw new InvalidOperationException("报告数据不可用，无法导出。");
        }

        string ext = format.ToLowerInvariant() switch
        {
            "html" => "html",
            "txt" => "txt",
            "json" => "json",
            _ => throw new ArgumentException($"不支持的导出格式：{format}"),
        };

        var report = _report;

        string path = await Task.Run(() =>
        {
            Directory.CreateDirectory(ReportsDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string file = Path.Combine(ReportsDir, $"OptTool-report-{stamp}.{ext}");

            string content = ext switch
            {
                "html" => BuildHtml(report),
                "txt" => BuildTxt(report),
                "json" => JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
                _ => throw new InvalidOperationException(),
            };

            File.WriteAllText(file, content);
            return file;
        });

        LastExportPath = path;
        StatusText = $"已导出 {ext.ToUpperInvariant()}：{path}";
        return path;
    }

    private static string BuildTxt(CheckupReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.AppName);
        sb.AppendLine($"生成时间：{r.GeneratedAt}");
        sb.AppendLine();
        sb.AppendLine($"操作系统：{r.Os}（版本 {r.OsVersion}）");
        sb.AppendLine($"内部版本：{r.OsBuild}");
        sb.AppendLine($"处理器  ：{r.Cpu}");
        sb.AppendLine($"逻辑核心：{r.LogicalProcessors}");
        sb.AppendLine($"内存    ：{CleanScanService.FormatSize(r.MemoryTotalBytes - r.MemoryAvailableBytes)} / " +
                      $"{CleanScanService.FormatSize(r.MemoryTotalBytes)}（占用 {r.MemoryUsagePercent:F0}%）");
        sb.AppendLine();
        sb.AppendLine("磁盘卷：");
        foreach (var d in r.Disks)
        {
            sb.AppendLine($"  · {d.Drive} [{d.Label}] 可用 {CleanScanService.FormatSize(d.FreeBytes)} / " +
                          $"共 {CleanScanService.FormatSize(d.TotalBytes)}");
        }

        sb.AppendLine();
        sb.AppendLine("图形适配器：");
        foreach (var a in r.Adapters)
        {
            string kind = a.IsSoftware ? "软件" : "硬件";
            string vram = a.IsSoftware ? "" : $"，{CleanScanService.FormatSize((long)a.DedicatedVideoBytes)} 显存";
            sb.AppendLine($"  · [{kind}] {a.Description}{vram}（0x{a.VendorId:X4}:0x{a.DeviceId:X4}）");
        }

        sb.AppendLine();
        sb.AppendLine($"D3D12 特性等级：{r.FeatureLevels}");
        sb.AppendLine($"硬件加速 GPU 调度：{r.Hags}");
        sb.AppendLine($"提权状态：{r.Elevation}");
        sb.AppendLine();
        sb.AppendLine("本报告由只读检测生成，未对系统做任何修改。");
        return sb.ToString().TrimEnd();
    }

    private static string BuildHtml(CheckupReport r)
    {
        static string Esc(string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);

        var diskRows = new StringBuilder();
        foreach (var d in r.Disks)
        {
            diskRows.AppendLine(
                $"<tr><td>{Esc(d.Drive)}</td><td>{Esc(d.Label)}</td>" +
                $"<td>{Esc(CleanScanService.FormatSize(d.FreeBytes))}</td>" +
                $"<td>{Esc(CleanScanService.FormatSize(d.TotalBytes))}</td></tr>");
        }

        var adapterRows = new StringBuilder();
        foreach (var a in r.Adapters)
        {
            string kind = a.IsSoftware ? "软件" : "硬件";
            string vram = a.IsSoftware ? "—" : CleanScanService.FormatSize((long)a.DedicatedVideoBytes);
            adapterRows.AppendLine(
                $"<tr><td>{Esc(a.Description)}</td><td>{kind}</td>" +
                $"<td>{Esc(vram)}</td><td>0x{a.VendorId:X4}:0x{a.DeviceId:X4}</td></tr>");
        }

        string featureHtml = Esc(r.FeatureLevels.Trim()).Replace("\n", "<br />");

        return $$"""
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8" />
<title>OptTool 体检报告</title>
<style>
  body { font-family: "Segoe UI", "Microsoft YaHei", sans-serif; margin: 32px; color: #1b1b1b; background: #fafafa; }
  h1 { font-size: 24px; margin-bottom: 4px; }
  .meta { color: #666; font-size: 13px; margin-bottom: 24px; }
  h2 { font-size: 17px; margin-top: 28px; border-left: 4px solid #2b6fff; padding-left: 8px; }
  table { border-collapse: collapse; width: 100%; background: #fff; }
  th, td { border: 1px solid #e2e2e2; padding: 8px 12px; font-size: 13px; text-align: left; }
  th { background: #f0f3fb; }
  .kv { background: #fff; border: 1px solid #e2e2e2; border-radius: 8px; padding: 4px 16px; }
  .kv p { font-size: 13px; margin: 10px 0; }
  .note { margin-top: 28px; color: #555; font-size: 12px; }
</style>
</head>
<body>
  <h1>OptTool 体检报告</h1>
  <div class="meta">生成时间：{{Esc(r.GeneratedAt)}}</div>

  <div class="kv">
    <p><strong>操作系统：</strong>{{Esc(r.Os)}}（版本 {{Esc(r.OsVersion)}}）</p>
    <p><strong>内部版本：</strong>{{Esc(r.OsBuild)}}</p>
    <p><strong>处理器：</strong>{{Esc(r.Cpu)}}</p>
    <p><strong>逻辑核心：</strong>{{r.LogicalProcessors}}</p>
    <p><strong>内存：</strong>{{Esc(CleanScanService.FormatSize(r.MemoryTotalBytes - r.MemoryAvailableBytes))}} /
       {{Esc(CleanScanService.FormatSize(r.MemoryTotalBytes))}}（占用 {{r.MemoryUsagePercent:F0}}%）</p>
    <p><strong>硬件加速 GPU 调度：</strong>{{Esc(r.Hags)}}</p>
    <p><strong>提权状态：</strong>{{Esc(r.Elevation)}}</p>
  </div>

  <h2>磁盘卷</h2>
  <table>
    <tr><th>卷</th><th>卷标</th><th>可用空间</th><th>总容量</th></tr>
    {{diskRows.ToString().Trim()}}
  </table>

  <h2>图形适配器</h2>
  <table>
    <tr><th>适配器</th><th>类型</th><th>专用显存</th><th>厂商:设备</th></tr>
    {{adapterRows.ToString().Trim()}}
  </table>

  <h2>D3D12 特性等级</h2>
  <div class="kv"><p>{{featureHtml}}</p></div>

  <p class="note">本报告由只读检测生成，未对系统做任何修改；OptTool 不联网、不上传数据。</p>
</body>
</html>
""";
    }
}
