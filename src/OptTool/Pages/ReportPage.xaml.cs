using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OptTool.ViewModels;

namespace OptTool.Pages;

public sealed partial class ReportPage : Page
{
    public ReportViewModel ViewModel { get; } = new();

    private bool _loadedOnce;

    public ReportPage()
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
        StatusBar.Title = "汇总状态";
        StatusBar.Message = "正在汇总体检结果…";

        try
        {
            await ViewModel.LoadAsync();

            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Title = "汇总完成";
            StatusBar.Message = ViewModel.StatusText;
        }
        catch (Exception ex)
        {
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Title = "汇总失败";
            StatusBar.Message = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            LoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    private async Task ExportAsync(string format)
    {
        try
        {
            string path = await ViewModel.ExportAsync(format);

            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Title = "导出成功";
            StatusBar.Message = path;
        }
        catch (Exception ex)
        {
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Title = "导出失败";
            StatusBar.Message = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private void ExportHtml_Click(object sender, RoutedEventArgs e) => _ = ExportAsync("html");

    private void ExportTxt_Click(object sender, RoutedEventArgs e) => _ = ExportAsync("txt");

    private void ExportJson_Click(object sender, RoutedEventArgs e) => _ = ExportAsync("json");
}
