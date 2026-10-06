using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OptTool.Pages;

namespace OptTool;

public sealed partial class MainWindow : Window
{
    /// <summary>路由表（见 ARCHITECTURE.md §3.1）：tag ↔ 页面类型，双向使用。</summary>
    private static readonly Dictionary<string, Type> Routes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dashboard"] = typeof(DashboardPage),
        ["clean"] = typeof(CleanPage),
        ["optimize"] = typeof(OptimizePage),
        ["game"] = typeof(GamePage),
        ["undo"] = typeof(UndoPage),
        ["report"] = typeof(ReportPage),
        ["settings"] = typeof(SettingsPage),
    };

    /// <summary>防止「用户点侧栏 → 导航 → 回设选中项」形成回环。</summary>
    private bool _syncing;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        // 关键：以 Frame 的实际导航为唯一权威来源，侧栏选中态反过来跟随它。
        // 不能反过来依赖 SelectionChanged 驱动导航——NavigationView 在初始化与
        // 布局期间会自行触发 SelectionChanged，且此时 Frame 尚未完成导航，
        // 会导致侧栏高亮与实际内容不同步（实测会随机落在「清理释放」或「设置」）。
        NavFrame.Navigating += NavFrame_Navigating;

        // 首屏延迟到布局稳定后再导航，避免与 NavigationView 的初始化选择竞争。
        // 直接使用 SelectedItem 赋值会与布局过程抢时序。
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_initialNavigated)
            {
                return;
            }
            _initialNavigated = true;
            NavFrame.Navigate(typeof(DashboardPage));
        });
    }

    private bool _initialNavigated;

    /// <summary>按 tag 找到侧栏项。</summary>
    private NavigationViewItem? FindMenuItem(string tag)
    {
        foreach (var menuItem in NavView.MenuItems)
        {
            if (menuItem is NavigationViewItem navItem &&
                navItem.Tag is string itemTag &&
                string.Equals(itemTag, tag, StringComparison.OrdinalIgnoreCase))
            {
                return navItem;
            }
        }
        return null;
    }

    /// <summary>由页面类型反查 tag（路由表反向查找）。</summary>
    private static string? TagForPage(Type pageType)
    {
        foreach (var kv in Routes)
        {
            if (kv.Value == pageType)
            {
                return kv.Key;
            }
        }
        return null;
    }

    /// <summary>Frame 实际导航时，把侧栏选中态同步过去。</summary>
    private void NavFrame_Navigating(object sender, NavigatingCancelEventArgs e)
    {
        if (e.SourcePageType is null)
        {
            return;
        }

        string? tag = TagForPage(e.SourcePageType);
        if (tag is null)
        {
            return;
        }

        var item = FindMenuItem(tag);
        if (item is null || ReferenceEquals(NavView.SelectedItem, item))
        {
            return;
        }

        _syncing = true;
        try
        {
            NavView.SelectedItem = item;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (NavFrame.CanGoBack)
        {
            NavFrame.GoBack();
        }
    }

    /// <summary>
    /// 用户点击侧栏时导航。未知 tag 静默忽略（不得抛异常）。
    /// 由 _syncing 拦截「同步选中态」引发的回环。
    /// </summary>
    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncing)
        {
            return;
        }

        if (args.SelectedItem is not NavigationViewItem item ||
            item.Tag is not string tag ||
            !Routes.TryGetValue(tag, out Type? page))
        {
            return;
        }

        if (NavFrame.CurrentSourcePageType != page)
        {
            NavFrame.Navigate(page);
        }
    }
}
