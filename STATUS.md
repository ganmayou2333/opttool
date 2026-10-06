# 项目状态与交接说明

> 快照时间：2026-10-06 22:00 ｜ 对应提交 `7280fb5`
> 仓库：https://github.com/ganmayou2333/opttool

## 一句话现状

**第一版 demo 的核心（体检仪表盘）已跑通并显示真实数据；骨架、服务层、测试、导航均已完成。
剩余 6 个页面存在已定位的显示缺陷，修法明确，是下一步的唯一工作。**

---

## 1. 已验证可用的部分

### 体检仪表盘（DashboardPage）

实测截图确认显示真实数据：

| 卡片 | 实际显示 |
|---|---|
| 操作系统 | `Windows 11`（Win11 识别修复生效，未误报 Win10） |
| 处理器 | `AMD Ryzen 9 8940HX with Radeon Graphics` |
| 内存占用 | `85%` · 已用 13.1 GB / 共 15.2 GB |
| 显卡 | `NVIDIA GeForce RTX 5070 Laptop GPU` · 2 个硬件适配器 · 最高 7891 MB |
| DirectX 特性等级 | `12_2` · 硬件加速 GPU 调度：已启用 |
| 检测明细 | 3 个适配器（含厂商/设备 ID、显存），软件适配器正确标注 |

### 服务层与测试

| 项目 | 状态 |
|---|---|
| `dotnet build` | **0 错误 0 警告** |
| `dotnet test` | **41 / 41 通过** |
| `dotnet publish` | 成功，`dist\OptTool.pri` 存在，程序可启动 |
| 禁止项自查（WMI / PowerShell / wmic / Assembly.Location） | 0 命中 |

### 导航

以 `Frame.Navigating` 为权威来源同步侧栏选中态，首屏延迟到布局稳定后导航。
7 个导航项切换均不崩溃，首屏正确落在仪表盘。

---

## 2. 已定位待修的缺陷（下一步唯一工作）

### 6 个页面卡在"加载中"

涉及：`CleanPage`、`OptimizePage`、`GamePage`、`UndoPage`、`ReportPage`、`SettingsPage`

**根因 1：x:Bind 通知键与绑定路径不匹配**

这 6 个页面写的是 `{x:Bind ViewModel.Foo, Mode=OneWay}`。
WinUI 的 `x:Bind` 用**完整绑定路径**作为 `PropertyChanged` 的通知键，即它监听 `"ViewModel.Foo"`；
而 `ObservableObject.OnPropertyChanged` 发出的键是 `"Foo"`。两者不匹配 → 界面永不更新。

**根因 2：后台线程更新绑定属性抛 COMException**

采集在 `Task.Run` 后台线程执行时，若直接给被绑定属性赋值，WinUI 会抛 `COMException`
（仪表盘实测报过 `检测失败 COMException`）。

**修法（`DashboardPage` 已是标准范例）**

1. 把被绑定属性**搬到 Page 自身**，让 Page 实现 `INotifyPropertyChanged`：

```csharp
public sealed partial class XxxPage : Page, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(T value, [CallerMemberName] string? name = null)
    {
        if (name is null) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private string _foo = "加载中…";
    public string Foo { get => _foo; private set { _foo = value; Set(value); } }
}
```

2. XAML 去掉 `ViewModel.` 前缀：`{x:Bind Foo, Mode=OneWay}`

3. 采集仍在后台线程，但属性赋值经 `DispatcherQueue` 切回 UI 线程。
   推荐**采集阶段只写局部变量，最后一次性提交**：

```csharp
var dispatcher = DispatcherQueue;
await Task.Run(() =>
{
    void Post(Action a) => dispatcher.TryEnqueue(() => a());
    Collect(Post);   // Collect 内部只算局部变量，末尾一次 Post 全部属性
});
```

4. 集合类数据（启动项列表、历史记录）同样搬到 Page 上，用
   `ObservableCollection<T>` 或 `IReadOnlyList<T>`，在 UI 线程整体赋值；
   `ListView` 用 `{x:Bind ItemsSource, Mode=OneWay}`，**不要在 ItemTemplate 里绑 `ViewModel.X`**。

**自查命令**（输出必须为空）：

```powershell
Select-String -Path (Get-ChildItem src\OptTool\Pages -Recurse -Include *.xaml).FullName `
  -Pattern 'x:Bind\s+ViewModel\.'
```

---

## 3. 关键踩坑记录（避免重复踩）

### 发布必须校验 `.pri`

WinUI 3 非打包 + 自包含发布时，应用自己的 `.pri` 资源**不会**被自动复制到发布目录。
缺失时**构建照样成功，但程序启动即崩**（`0xC000027B` / Stowed Exception，崩溃模块 `Microsoft.UI.Xaml.dll`）。

`OptTool.csproj` 中的 `CopyPriToPublish` 目标负责复制，并在源文件缺失时用 `<Error>` 让构建**失败**（而非静默产出坏包）。

### 构建前置条件

- .NET SDK 10
- MSVC 链接器（VS 生成工具的 C++ 桌面开发工作负载）
- **Windows SDK 完整 um 库**：仅装 ucrt 部分会报
  `LINK : fatal error LNK1181: 无法打开输入文件"advapi32.lib"`，
  需补装 `Microsoft.VisualStudio.Component.Windows11SDK.26100`

### VS 安装器限制

`--quiet` / `--passive` **要求进程从启动时即已提权**，否则退出码 **5007**。
必须用 `Start-Process -Verb RunAs`。且含空格的 `--installPath` 需要内层引号，
否则被截断成 `C:\Program`。

### WinForms 不可用于 Native AOT

本项目早期曾计划 `C# Native AOT + WinForms`，官方文档明确否决：

> **Windows Forms** … is heavily reliant on built-in COM marshalling, so
> **trimming support for Windows Forms apps is disabled in the .NET SDK currently.**
> —— [Known trimming incompatibilities](https://learn.microsoft.com/dotnet/core/deploying/trimming/incompatibilities#windows-platform-incompatibilities)

而 Native AOT 的限制包含 `Windows: No built-in COM` 与 `Requires trimming`，两者互斥。
故改用 **WinUI 3**（它本身也不支持 Native AOT，因此走完整 .NET 运行时）。

### 其他

- Windows 11 上注册表 `ProductName` 仍写 `Windows 10`，必须用
  `CurrentBuildNumber >= 22000` 判定（`SystemInfoService` 已处理）
- DXGI 枚举同一块显卡可能返回多个不同 LUID 的条目，
  实测 RTX 5070 出现两次；去重键应用「厂商 ID + 设备 ID + 专用显存」，**LUID 不可靠**
- `Microsoft Basic Render Driver` 通常**不设置** `DXGI_ADAPTER_FLAG_SOFTWARE`，
  需按厂商 ID `0x1414` 与名称兜底判定
- 含中文的 `.ps1` 在 Windows PowerShell 5.1 下必须带 **UTF-8 BOM**，否则语法会崩

---

## 4. 开发协作方式

- **豆包**：负责界面与功能的实现（通过 dsh 连接器派发，`work` 模式，需开 9222 调试端口）
- **Lead**：负责架构决策、任务分解、独立验证与 Bug 定位

`reports/` 下每份报告都含**真实命令与真实输出**的验证记录，可作为过程证据。

---

## 5. 下一步建议（按优先级）

1. **修 6 个页面的绑定缺陷**（修法与自查命令见 §2），修完逐页截图确认不再显示"加载中"
2. 补 `DashboardPage` 的 UI 层回归测试（当前 41 个测试只覆盖服务层）
3. 接入「一键优化」的真实执行（走五步闭环，仅低风险项）
4. 清理模块从"只扫描不执行"推进到"可执行 + 可回滚"

## 6. 当前环境提醒

- 豆包的调试端口（9222）若仍开着，**本机任意进程都能接管它**；不用时退出豆包并正常启动即可关闭
- 本机 `github.com:443` 存在**间歇性阻断**，推送/拉取失败时重试几次即可成功
- 本仓库提交身份已设为 `186293913+ganmayou2333@users.noreply.github.com`
  （GitHub 邮箱隐私保护会拒绝非 noreply 邮箱的推送）
