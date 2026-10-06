using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OptTool.ViewModels;

namespace OptTool.Pages;

public sealed partial class CleanPage : Page
{
    public CleanViewModel ViewModel { get; } = new();

    private bool _loadedOnce;

    public CleanPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 只在首次进入页面时扫描，来回切页不重复扫描。
        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;
        await RunScanAsync();
    }

    private async Task RunScanAsync()
    {
        ExecuteButton.IsEnabled = false;
        LoadingBar.Visibility = Visibility.Visible;
        StatusBar.Severity = InfoBarSeverity.Informational;
        StatusBar.Title = "扫描状态";
        StatusBar.Message = "正在扫描可清理空间…";

        try
        {
            await ViewModel.LoadAsync();

            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Title = "扫描完成";
            StatusBar.Message = ViewModel.StatusText;
        }
        catch (Exception ex)
        {
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Title = "扫描失败";
            StatusBar.Message = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            LoadingBar.Visibility = Visibility.Collapsed;
            ExecuteButton.IsEnabled = true;
        }
    }

    private async void ExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "执行清理",
            Content = "第一版暂不执行清理。\n\n本版本只提供「扫描 + 显示 + 勾选」；" +
                      "后续版本接入删除时，会先备份并保证每一步都可回滚。",
            CloseButtonText = "知道了",
        };

        await dialog.ShowAsync();
    }
}
