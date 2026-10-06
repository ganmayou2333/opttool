using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OptTool.ViewModels;

namespace OptTool.Pages;

public sealed partial class GamePage : Page
{
    public GameViewModel ViewModel { get; } = new();

    private bool _loadedOnce;

    public GamePage()
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
        StatusBar.Title = "采集状态";
        StatusBar.Message = "正在采集显卡与驱动信息…";

        try
        {
            await ViewModel.LoadAsync();

            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Title = "采集完成";
            StatusBar.Message = ViewModel.StatusText;
        }
        catch (Exception ex)
        {
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Title = "采集失败";
            StatusBar.Message = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            LoadingBar.Visibility = Visibility.Collapsed;
        }
    }
}
