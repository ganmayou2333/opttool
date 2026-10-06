using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OptTool.ViewModels;

namespace OptTool.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = new();

    private bool _loadedOnce;

    public SettingsPage()
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
        StatusBar.Message = "正在读取设置信息…";

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
}
