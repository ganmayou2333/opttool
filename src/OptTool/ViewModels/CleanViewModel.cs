using System.Collections.ObjectModel;
using OptTool.Services;

namespace OptTool.ViewModels;

/// <summary>单个可清理项的行视图模型。</summary>
public sealed class CleanItemViewModel : ObservableObject
{
    private bool _isChecked;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required long SizeBytes { get; init; }
    public required string SizeText { get; init; }
    public required string Detail { get; init; }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}

/// <summary>
/// 清理释放页视图模型。后台扫描各安全清理项的真实可释放空间，
/// 只扫描与展示，不执行删除（执行由后续版本提供）。
/// </summary>
public sealed class CleanViewModel : ObservableObject
{
    private bool _isLoading = true;
    private string _statusText = "正在扫描可清理空间…";
    private string _selectedSizeText = "已选 0 B（共 0 B）";
    private bool _windowsOldExists;
    private string _windowsOldText = "";

    public ObservableCollection<CleanItemViewModel> Items { get; } = new();

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

    public string SelectedSizeText
    {
        get => _selectedSizeText;
        private set => SetProperty(ref _selectedSizeText, value);
    }

    /// <summary>是否存在 Windows.old（只提示，不代删，见 ARCHITECTURE.md §7）。</summary>
    public bool WindowsOldExists
    {
        get => _windowsOldExists;
        private set => SetProperty(ref _windowsOldExists, value);
    }

    public string WindowsOldText
    {
        get => _windowsOldText;
        private set => SetProperty(ref _windowsOldText, value);
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = "正在扫描可清理空间…";

        try
        {
            // 后台线程扫描，返回纯数据，回到 UI 线程再填充集合。
            var scanned = await Task.Run(() =>
            {
                var rows = new List<CleanItemViewModel>();

                // 1. 用户临时目录 %TEMP%
                try
                {
                    string userTemp = Path.GetTempPath();
                    var r = CleanScanService.MeasureDirectory(userTemp);
                    rows.Add(new CleanItemViewModel
                    {
                        Id = "user-temp",
                        Name = "用户临时文件",
                        Description = $"当前用户 %TEMP% 目录：{userTemp.TrimEnd('\\')}",
                        SizeBytes = r.SizeBytes,
                        SizeText = CleanScanService.FormatSize(r.SizeBytes),
                        Detail = $"{r.FileCount} 个文件，跳过 {r.SkippedCount} 个占用中/无权限文件",
                    });
                }
                catch
                {
                    // 单项失败不影响整体。
                }

                // 2. 系统临时目录 C:\Windows\Temp
                try
                {
                    var r = CleanScanService.MeasureDirectory(@"C:\Windows\Temp");
                    rows.Add(new CleanItemViewModel
                    {
                        Id = "windows-temp",
                        Name = "系统临时文件",
                        Description = @"C:\Windows\Temp（系统级临时目录）",
                        SizeBytes = r.SizeBytes,
                        SizeText = CleanScanService.FormatSize(r.SizeBytes),
                        Detail = $"{r.FileCount} 个文件，跳过 {r.SkippedCount} 个占用中/无权限文件",
                    });
                }
                catch { }

                // 3. 回收站
                try
                {
                    var (size, count) = CleanScanService.QueryRecycleBin();
                    rows.Add(new CleanItemViewModel
                    {
                        Id = "recycle-bin",
                        Name = "回收站",
                        Description = "全部驱动器回收站中的已删除文件",
                        SizeBytes = size,
                        SizeText = CleanScanService.FormatSize(size),
                        Detail = $"{count} 个项目",
                    });
                }
                catch { }

                // 4. 缩略图 / 图标缓存
                try
                {
                    var r = CleanScanService.MeasureThumbnailCache();
                    rows.Add(new CleanItemViewModel
                    {
                        Id = "thumb-cache",
                        Name = "缩略图与图标缓存",
                        Description = @"资源管理器的 thumbcache / iconcache 缓存，删除后会自动重建",
                        SizeBytes = r.SizeBytes,
                        SizeText = CleanScanService.FormatSize(r.SizeBytes),
                        Detail = $"{r.FileCount} 个缓存文件",
                    });
                }
                catch { }

                // 5. 崩溃转储
                try
                {
                    var r = CleanScanService.MeasureCrashDumps();
                    rows.Add(new CleanItemViewModel
                    {
                        Id = "crash-dumps",
                        Name = "崩溃转储文件",
                        Description = "应用崩溃转储（*.dmp）与系统小内存转储，仅供排错使用",
                        SizeBytes = r.SizeBytes,
                        SizeText = CleanScanService.FormatSize(r.SizeBytes),
                        Detail = $"{r.FileCount} 个转储文件，跳过 {r.SkippedCount} 个无权限文件",
                    });
                }
                catch { }

                bool winOld = false;
                try
                {
                    winOld = Directory.Exists(@"C:\Windows.old");
                }
                catch { }

                return (rows, winOld);
            });

            Items.Clear();
            foreach (var row in scanned.rows)
            {
                row.PropertyChanged += OnItemPropertyChanged;
                Items.Add(row);
            }

            WindowsOldExists = scanned.winOld;
            WindowsOldText = scanned.winOld
                ? "检测到 C:\\Windows.old（旧系统备份）。本工具不代删；如需释放空间，请通过「设置 → 系统 → 存储 → 临时文件」处理。"
                : "";

            RecomputeSelected();
            StatusText = $"扫描完成，共 {Items.Count} 个安全清理项。第一版仅扫描与展示，不执行删除。";
        }
        catch (Exception ex)
        {
            StatusText = $"扫描失败：{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanItemViewModel.IsChecked))
        {
            RecomputeSelected();
        }
    }

    private void RecomputeSelected()
    {
        long selected = 0;
        long total = 0;

        foreach (var item in Items)
        {
            total += item.SizeBytes;
            if (item.IsChecked)
            {
                selected += item.SizeBytes;
            }
        }

        SelectedSizeText = $"已选 {CleanScanService.FormatSize(selected)}（共 {CleanScanService.FormatSize(total)}）";
    }
}
