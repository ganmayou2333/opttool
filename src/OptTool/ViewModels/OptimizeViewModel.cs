using System.Collections.ObjectModel;
using OptTool.Services;

namespace OptTool.ViewModels;

/// <summary>启动项行视图模型。</summary>
public sealed class StartupItemViewModel : ObservableObject
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required string Source { get; init; }
    public required bool IsProtected { get; init; }
    public required string StatusText { get; init; }
}

/// <summary>
/// 优化调整页视图模型。列出真实启动项并用 GuardList 标注受保护项。
/// 第一版只展示，不修改注册表。
/// </summary>
public sealed class OptimizeViewModel : ObservableObject
{
    private bool _isLoading = true;
    private string _statusText = "正在读取启动项…";
    private string _summaryText = "";

    public ObservableCollection<StartupItemViewModel> Items { get; } = new();

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

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = "正在读取启动项…";

        try
        {
            var rows = await Task.Run(() =>
            {
                var result = new List<StartupItemViewModel>();
                IReadOnlyList<Models.StartupEntry> entries;
                try
                {
                    entries = new SystemInfoService().GetStartupEntries();
                }
                catch
                {
                    entries = Array.Empty<Models.StartupEntry>();
                }

                foreach (var entry in entries)
                {
                    bool isProtected = false;
                    string protectReason = "";

                    try
                    {
                        string? path = ExtractExecutablePath(entry.Command);
                        if (path is not null)
                        {
                            var (allowed, reason) = GuardList.IsPathAllowed(path);
                            isProtected = !allowed;
                            protectReason = reason;
                        }
                    }
                    catch { }

                    result.Add(new StartupItemViewModel
                    {
                        Name = entry.Name,
                        Command = string.IsNullOrWhiteSpace(entry.Command) ? "（无命令行）" : entry.Command,
                        Source = entry.Source,
                        IsProtected = isProtected,
                        StatusText = isProtected
                            ? $"受保护，不可修改（{protectReason}）"
                            : "可修改（第一版仅展示，不执行改动）",
                    });
                }

                return result;
            });

            Items.Clear();
            foreach (var row in rows)
            {
                Items.Add(row);
            }

            int protectedCount = rows.Count(r => r.IsProtected);
            SummaryText = $"共 {rows.Count} 个启动项，其中 {protectedCount} 个受 GuardList 保护。";
            StatusText = "读取完成。第一版只展示，不修改注册表。";
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
    /// 从启动项命令行中提取目标可执行文件路径。
    /// 支持引号包裹、环境变量展开、路径含空格（逐 token 累加直到文件存在）。
    /// </summary>
    private static string? ExtractExecutablePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        string expanded;
        try
        {
            expanded = Environment.ExpandEnvironmentVariables(command.Trim());
        }
        catch
        {
            expanded = command.Trim();
        }

        if (expanded[0] == '"')
        {
            int end = expanded.IndexOf('"', 1);
            if (end > 1)
            {
                return expanded.Substring(1, end - 1);
            }
        }

        string[] tokens = expanded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? acc = null;

        foreach (var token in tokens)
        {
            // 遇到参数（- 或 / 开头）说明可执行路径部分已结束。
            if (acc is not null && (token.StartsWith('-') || token.StartsWith('/')))
            {
                break;
            }

            acc = acc is null ? token : acc + " " + token;

            try
            {
                if (File.Exists(acc))
                {
                    return acc;
                }
            }
            catch { }
        }

        return acc;
    }
}
