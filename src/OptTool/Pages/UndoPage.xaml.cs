using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OptTool.ViewModels;

namespace OptTool.Pages;

public sealed partial class UndoPage : Page
{
    public UndoViewModel ViewModel { get; } = new();

    private bool _loadedOnce;

    public UndoPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;
        await RunLoadAsync();
    }

    private async Task RunLoadAsync()
    {
        LoadingBar.Visibility = Visibility.Visible;
        StatusBar.Severity = InfoBarSeverity.Informational;
        StatusBar.Title = "读取状态";
        StatusBar.Message = "正在读取操作历史…";

        try
        {
            await ViewModel.LoadAsync();

            StatusBar.Severity = ViewModel.HasRecords ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
            StatusBar.Title = "读取完成";
            StatusBar.Message = ViewModel.StatusText;
        }
        catch (Exception ex)
        {
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Title = "读取失败";
            StatusBar.Message = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            LoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    private async void RollbackButton_Click(object sender, RoutedEventArgs e)
    {
        var row = (sender as FrameworkElement)?.DataContext as HistoryRowViewModel;
        string name = row?.OperationName ?? "该操作";

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "撤销操作",
            Content = $"第一版暂不执行回滚（{name}）。\n\n后续版本接入回滚时，会按 history.jsonl 中记录的原值逐项还原。",
            CloseButtonText = "知道了",
        };

        await dialog.ShowAsync();
    }
}
