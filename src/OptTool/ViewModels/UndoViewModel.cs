using System.Collections.ObjectModel;
using OptTool.Services;

namespace OptTool.ViewModels;

/// <summary>历史操作行。</summary>
public sealed class HistoryRowViewModel : ObservableObject
{
    public required string Id { get; init; }
    public required string TimeText { get; init; }
    public required string OperationName { get; init; }
    public required string RiskLevel { get; init; }
    public required int ChangeCount { get; init; }
    public string ChangeCountText => $"{ChangeCount} 项变更（Id：{Id}）";
}

/// <summary>
/// 撤销中心页视图模型。读取 data\history.jsonl 中的操作历史。
/// 第一版只展示，不执行回滚。
/// </summary>
public sealed class UndoViewModel : ObservableObject
{
    private bool _isLoading = true;
    private string _statusText = "正在读取操作历史…";
    private bool _hasRecords;
    private bool _isEmpty = true;

    public ObservableCollection<HistoryRowViewModel> Records { get; } = new();

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

    public bool HasRecords
    {
        get => _hasRecords;
        private set
        {
            if (SetProperty(ref _hasRecords, value))
            {
                SetProperty(ref _isEmpty, !value, nameof(IsEmpty));
            }
        }
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = "正在读取操作历史…";

        try
        {
            var rows = await Task.Run(() =>
            {
                var result = new List<HistoryRowViewModel>();
                IReadOnlyList<OperationRecord> records;
                try
                {
                    records = new OperationHistory().ReadAll();
                }
                catch
                {
                    records = Array.Empty<OperationRecord>();
                }

                // 最新的记录排在最前。
                for (int i = records.Count - 1; i >= 0; i--)
                {
                    var r = records[i];
                    string risk = string.IsNullOrWhiteSpace(r.RiskLevel) ? "未标注" : r.RiskLevel;

                    result.Add(new HistoryRowViewModel
                    {
                        Id = r.Id,
                        TimeText = r.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                        OperationName = r.OperationName,
                        RiskLevel = risk,
                        ChangeCount = r.Changes.Count,
                    });
                }

                return result;
            });

            Records.Clear();
            foreach (var row in rows)
            {
                Records.Add(row);
            }

            HasRecords = rows.Count > 0;
            StatusText = HasRecords
                ? $"共 {rows.Count} 条操作记录。第一版仅展示，不执行回滚。"
                : "暂无操作历史。";
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
}
