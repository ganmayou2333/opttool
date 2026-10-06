using System.Collections.ObjectModel;
using OptTool.Services;

namespace OptTool.ViewModels;

/// <summary>
/// 设置页视图模型。展示数据目录、便携模式可写性、提权状态、备份上限与风险声明。
/// 全部为只读信息。
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    /// <summary>备份体积上限默认值（MB）。</summary>
    public const long BackupSizeLimitMB = 200;

    private bool _isLoading = true;
    private string _statusText = "正在读取设置信息…";
    private string _portableModeText = "检测中…";
    private string _elevationText = "检测中…";
    private string _dataDirSizeText = "检测中…";
    private bool _exeDirWritable;
    private bool _exeDirNotWritable = true;

    public string BaseDir => AppContext.BaseDirectory;

    public string DataDir => Path.Combine(AppContext.BaseDirectory, "data");

    public string BackupLimitText => $"{BackupSizeLimitMB} MB（默认上限，超出后最旧备份将被清理）";

    public ObservableCollection<string> RiskStatements { get; } = new()
    {
        "不联网：程序不发起任何网络连接",
        "不上传数据：所有检测结果与备份只保存在本机",
        "不做静默修改：任何系统改动前都会明确提示并等待确认",
        "所有改动可回滚：执行前先备份，可在「撤销中心」还原",
    };

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

    public string PortableModeText
    {
        get => _portableModeText;
        private set => SetProperty(ref _portableModeText, value);
    }

    public string ElevationText
    {
        get => _elevationText;
        private set => SetProperty(ref _elevationText, value);
    }

    public string DataDirSizeText
    {
        get => _dataDirSizeText;
        private set => SetProperty(ref _dataDirSizeText, value);
    }

    public bool ExeDirWritable
    {
        get => _exeDirWritable;
        private set
        {
            if (SetProperty(ref _exeDirWritable, value))
            {
                SetProperty(ref _exeDirNotWritable, !value, nameof(ExeDirNotWritable));
            }
        }
    }

    public bool ExeDirNotWritable
    {
        get => _exeDirNotWritable;
        private set => SetProperty(ref _exeDirNotWritable, value);
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = "正在读取设置信息…";

        try
        {
            var (writable, elevatedText, dataSizeText) = await Task.Run(() =>
            {
                bool canWrite = CheckWritable(AppContext.BaseDirectory);

                string elev;
                try
                {
                    var e = new SystemInfoService().GetElevation();
                    elev = e.IsElevated
                        ? $"已以管理员身份运行（{e.Reason}）"
                        : $"普通权限运行（{e.Reason}）";
                }
                catch
                {
                    elev = "提权状态不可用";
                }

                string size;
                try
                {
                    var scan = CleanScanService.MeasureDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
                    size = $"{CleanScanService.FormatSize(scan.SizeBytes)}（{scan.FileCount} 个文件）";
                }
                catch
                {
                    size = "不可用";
                }

                return (canWrite, elev, size);
            });

            ExeDirWritable = writable;
            PortableModeText = writable
                ? "便携模式：exe 目录可写，数据保存在程序目录下的 data 文件夹中。"
                : "exe 目录不可写（可能位于受保护位置）。数据目录写入前会提示并提供回退位置，不会静默失败。";
            ElevationText = elevatedText;
            DataDirSizeText = dataSizeText;

            StatusText = "设置信息读取完成。";
        }
        catch (Exception ex)
        {
            StatusText = $"读取失败：{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 便携模式判断：在 exe 目录尝试创建并删除一个临时探针文件，
    /// 成功即可写。任何异常都视为不可写，不抛出。
    /// </summary>
    private static bool CheckWritable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, $"writeprobe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(path: probe, contents: "t");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
