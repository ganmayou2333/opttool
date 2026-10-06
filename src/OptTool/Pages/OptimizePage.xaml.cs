using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OptTool.ViewModels;

namespace OptTool.Pages;

public sealed partial class OptimizePage : Page
{
    public OptimizeViewModel ViewModel { get; } = new();

    private bool _loadedOnce;

    public OptimizePage()
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
        StatusBar.Message = "正在读取启动项…";

        try
        {
            await ViewModel.LoadAsync();

            StatusBar.Severity = InfoBarSeverity.Success;
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

    private async void ModifyButton_Click(object sender, RoutedEventArgs e)
    {
        string name = (sender as FrameworkElement)?.Tag as string ?? "该启动项";

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "修改启动项",
            Content = $"第一版暂不执行修改（{name}）。\n\n本版本只展示启动项与保护状态；" +
                      "后续版本接入禁用/删除时，会先备份原值并保证可恢复。",
            CloseButtonText = "知道了",
        };

        await dialog.ShowAsync();
    }
}
