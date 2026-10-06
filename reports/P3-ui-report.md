# P3 — OptTool 界面实现报告（导航修复 + 6 页面真实内容）

- 任务 id：t-muwpc2ek-7c4918
- 工程：`G:\cft\src\OptTool`（WinUI 3 / net10.0-windows10.0.26100.0 / 非打包自包含）
- 日期：2026-10-06
- 边界遵守：本轮只写 WinUI 界面代码与只读扫描；**未执行任何真实清理/优化/回滚**，未调用 doubao-pc-optimizer。

---

## 1. 导航同步缺陷修复

### 根因
`MainWindow.xaml` 原先给「体检仪表盘」写了 `IsSelected="True"`。该设置在 `MenuItems` 集合构造期间不可靠，实测侧栏高亮会停在「游戏环境」而内容框架显示别的页面。

### 修复方式
- XAML 侧 `IsSelected="True"` 已移除（开工时确认已不在）。
- 在 `MainWindow.xaml.cs` 新增 `SelectMenuItem(string tag)`：遍历 `NavView.MenuItems`，匹配 `NavigationViewItem.Tag`，显式设置 `NavView.SelectedItem`；找不到对应项时静默返回，不抛异常。
- 在构造函数里 `NavFrame.Navigate(typeof(DashboardPage))` **之后**调用 `SelectMenuItem("dashboard")`。
- `NavView_SelectionChanged` 保持原样：设置 SelectedItem 会触发该事件，但其中 `NavFrame.CurrentSourcePageType != page` 的判断使首屏不重复导航；「当前页相同不重复 Navigate」的去重逻辑未被破坏。

关键代码（MainWindow.xaml.cs）：

```csharp
// 首屏：体检仪表盘
NavFrame.Navigate(typeof(DashboardPage));
// 导航后显式设置侧栏选中项，避免高亮与内容不同步。
SelectMenuItem("dashboard");

private void SelectMenuItem(string tag)
{
    foreach (var menuItem in NavView.MenuItems)
    {
        if (menuItem is NavigationViewItem navItem &&
            navItem.Tag is string itemTag && itemTag == tag)
        {
            NavView.SelectedItem = navItem;
            return;
        }
    }
}
```

### 验证
UI 自动化（SelectionItemPattern.Select）逐个点击 7 个导航项，每次选择后进程均存活（见 §3.4）。启动 10 秒后侧栏高亮与内容区一致，未再出现停在「游戏环境」的现象。

---

## 2. 六个页面的数据来源与实现要点

统一约定：每页配一个 ViewModel（`ViewModels\` 下，继承新增的 `ObservableObject`，实现 INotifyPropertyChanged）；采集全部在 `Task.Run` 后台线程，先产出纯数据、回到 UI 线程再填充 `ObservableCollection`；每个采集点独立 try/catch，失败显示「不可用」，不向界面抛异常。页面布局参考 DashboardPage：标题 + 说明 + InfoBar + ProgressBar + 卡片/列表。

新增/改动文件：

| 文件 | 说明 |
|---|---|
| `Interop\NativeMethods.cs` | 新增 shell32 只读 API `SHQueryRecycleBin`（查回收站大小/项数，不清空） |
| `Services\CleanScanService.cs` | 只读扫描：目录大小（容错递归）、回收站、缩略图缓存、崩溃转储、字节格式化 |
| `ViewModels\ObservableObject.cs` | VM 基类（INotifyPropertyChanged + SetProperty） |
| `ViewModels\CleanViewModel.cs` | 清理释放页 VM + `CleanItemViewModel` |
| `ViewModels\OptimizeViewModel.cs` | 优化调整页 VM + `StartupItemViewModel` |
| `ViewModels\GameViewModel.cs` | 游戏环境页 VM + 适配器/驱动/特性三行类型 |
| `ViewModels\UndoViewModel.cs` | 撤销中心页 VM + `HistoryRowViewModel` |
| `ViewModels\ReportViewModel.cs` | 报告页 VM + `CheckupReport` 模型 + HTML/TXT/JSON 生成 |
| `ViewModels\SettingsViewModel.cs` | 设置页 VM |
| `Pages\*.xaml(.cs)` ×6 | 六个页面重写为真实内容 |

### 2.1 CleanPage — 清理释放
数据来源（全部真实扫描，无假数据）：

| 清理项 | 来源 / 扫描方式 |
|---|---|
| 用户临时文件 | `Path.GetTempPath()` → 容错递归统计（本机：`C:\Users\TomHu\AppData\Local\Temp`） |
| 系统临时文件 | `C:\Windows\Temp` 容错递归统计 |
| 回收站 | `SHQueryRecycleBin(null, …)` 汇总全部驱动器 |
| 缩略图与图标缓存 | `%LocalAppData%\Microsoft\Windows\Explorer` 下 `thumbcache_*.db` / `iconcache_*.db` |
| 崩溃转储 | `%LocalAppData%\CrashDumps`、`C:\Windows\Minidump`、`C:\Windows\MEMORY.DMP`（*.dmp） |

要点：
- 每项显示名称、说明、真实可释放字节数、文件数与跳过数、勾选框；顶部实时显示「已选 X（共 Y）」。
- 递归扫描逐目录/逐文件 catch：占用中、无权限的文件和目录跳过并计数，不因单个文件失败中断。
- 遵守 ARCHITECTURE.md §7：**不做 Prefetch、不调 cleanmgr、不碰 WinSxS**；Windows.old 仅在存在时用 InfoBar 提示并引导到「设置 → 系统 → 存储 → 临时文件」，不代删（本次扫描 `C:\Windows.old` 不存在，提示条未出现）。
- **本轮不执行删除**：「执行清理」点击后弹出「第一版暂不执行清理」。

本机实测（2026-10-06 21:4x）：共 **6.07 GB** — 用户临时 5.51 GB（26775 文件）、系统临时 163.96 MB（345 文件，跳过 2）、回收站 0 B（0 项）、缩略图缓存 66.44 MB（30 文件）、崩溃转储 342.58 MB（10 文件）。

### 2.2 OptimizePage — 优化调整
- 数据来源：`SystemInfoService.GetStartupEntries()`（HKCU/HKLM Run 键 + 用户/公共启动文件夹），显示名称、完整命令行、来源。
- 保护判定：从命令行解析目标可执行路径（支持引号、环境变量展开、路径含空格时逐 token 拼到文件真实存在），再用 `GuardList.IsPathAllowed(path)` 判定；被拦项显示「受保护，不可修改（原因）」并加「受保护」标记。
- 汇总行显示总数与受保护数。
- **不改注册表**：每行「修改」按钮弹出「第一版暂不执行修改」。
- 本机实测：**14 个启动项，0 个受 GuardList 保护**（含 Steam、iCloudServices、SecurityHealth、ACE-Tray、火绒 sysdiag 及两个启动文件夹的 desktop.ini 等，命令行均为真实读取）。

### 2.3 GamePage — 游戏环境
- 数据来源：`GpuInfoService.GetSummary()`（聚合 DXGI 适配器、注册表驱动、D3D12 探测、HAGS）。
- 显示：全部适配器并标注「硬件 GPU / 软件渲染」、厂商（VendorIdToName + ID）、专用显存；每条驱动的版本、日期、「已发布 N 天」，超过 365 天加「超过 365 天」标记并在顶部 Warning InfoBar 汇总提示可考虑更新；每个硬件适配器的 D3D12 支持情况与最高特性等级；HAGS 状态。
- 驱动去重：同一驱动在显示类注册表多个 `000x` 子键重复登记（本机「Microsoft Remote Display Adapter」有 5 条），在 **VM 展示层**按「描述+版本+日期」去重；未改 `GpuInfoService.GetDriverInfo()` 的公开 API。
- 页面顶部固定诚实声明 InfoBar：**「游戏优化实际收益通常只有几个百分点，瓶颈主要在硬件」**；并声明不注入游戏进程、不改游戏配置（§6.2）。
- 本机实测：2 硬件（RTX 5070 Laptop 7891 MB / 特性 12_2；AMD 610M 485 MB / 特性 12_1）+ 1 软件（Microsoft Basic Render Driver）；HAGS 已启用；RTX 驱动 19 天、AMD 47 天；Microsoft Remote Display Adapter 驱动日期 2006-06-21（7412 天）触发更新提示（该设备为系统虚拟显示，日期为系统占位值）。

### 2.4 UndoPage — 撤销中心
- 数据来源：`new OperationHistory().ReadAll()`（读 `data\history.jsonl`），显示本地时间、操作名、风险级别、变更项数与记录 Id；最新记录排最前。
- 空历史时显示友好空状态卡片（图标 + 「暂无操作历史」+ 引导去体检仪表盘），列表与空状态用 `HasRecords / IsEmpty` 两个 bool 控制 Visibility。
- 「撤销此操作」按钮弹出「第一版暂不执行回滚」。
- 本机实测：dist 下无 history.jsonl，正确显示空状态。

### 2.5 ReportPage — 报告
- 进入页面后用既有服务重新汇总：OS（含版本/内部版本）、CPU、内存、磁盘卷、全部适配器、D3D12 特性、HAGS、提权状态、生成时间，页面显示 TXT 预览。
- 三个导出按钮真实可用，导出目录 `Path.Combine(AppContext.BaseDirectory, "data", "reports")`（**用 AppContext.BaseDirectory 定位，未用 Assembly.Location**）；文件名 `OptTool-report-yyyyMMdd-HHmmss.{html|txt|json}`。
- HTML 自带内联 `<style>`（卡片、表格样式），双击即可用浏览器打开；JSON 用 `WriteIndented` 序列化；文本内容经 HTML 编码。
- 实测点击三个按钮均成功（见 §3.6）。

### 2.6 SettingsPage — 设置
- 数据目录：显示 `AppContext.BaseDirectory` 与 `data` 完整路径，并实测当前 data 目录大小（只读扫描）。
- exe 目录可写性（便携模式判断）：在 BaseDirectory 创建并删除 `writeprobe-<guid>.tmp` 探针文件，成功为「可写」，显示「便携模式：数据保存在程序目录 data 下」；失败则提示会提供回退位置、不静默失败。
- 提权状态：`SystemInfoService.GetElevation()`。
- 备份体积上限：显示默认 **200 MB** 及超限处理说明。
- 「关于」区块：五步闭环说明 + 风险声明四条（不联网、不上传数据、不做静默修改、所有改动可回滚）。
- 本机实测：exe 目录可写（`G:\cft\dist\`）、普通权限运行、data 6.01 KB（3 文件）。

### 可访问性附带改进
图标+文字面板形式的按钮 UIA 默认 Name 为空，已为全部操作按钮补 `AutomationProperties.Name`（执行清理、导出 HTML/TXT/JSON、重新检测、一键优化、修改启动项、撤销此操作），UI 自动化可稳定定位。

---

## 3. 验证记录（真命令真输出）

### 3.1 dotnet build → 0 错误 0 警告
命令：`dotnet build G:\cft\src\OptTool\OptTool.csproj -v q`
最终输出：

```
已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:19.44
```

（开工前基线 build 同为 0 警告 0 错误，00:00:16.56。）

### 3.2 dotnet test → 41 个全部通过
命令：`dotnet test G:\cft\src\OptTool.Tests -v q`
最终输出：

```
G:\cft\src\OptTool.Tests\bin\Debug\net10.0-windows10.0.26100.0\OptTool.Tests.dll (.NETCoreApp,Version=v10.0)的测试运行
总共 1 个测试文件与指定模式相匹配。

已通过! - 失败:     0，通过:    41，已跳过:     0，总计:    41，持续时间: 1 s - OptTool.Tests.dll (net10.0)
```

### 3.3 删除 dist 后 publish → 成功，OptTool.pri 存在
命令：`Remove-Item -Recurse -Force G:\cft\dist`；`dotnet publish G:\cft\src\OptTool\OptTool.csproj -c Release -o G:\cft\dist`
输出：

```
已还原 G:\cft\src\OptTool\OptTool.csproj (用时 727 毫秒)。
  OptTool -> G:\cft\src\OptTool\bin\Release\net10.0-windows10.0.26100.0\win-x64\OptTool.dll
  OptTool -> G:\cft\dist\
pri: True
```

### 3.4 启动 + 7 导航项不崩溃（方式：Windows UI 自动化客户端）
方式说明：通过 `Add-Type -AssemblyName UIAutomationClient/UIAutomationTypes`，按进程 Id 定位主窗口，对 7 个 `ControlType.ListItem` 导航项取 `SelectionItemPattern.Select()` 逐个选择，每项间隔 3 秒并检查进程存活。脚本：`G:\cft\reports\ui-nav-test.ps1`。
输出（最终版本）：

```
After 10s alive: True (pid 20464)
Window found: True
Window title: OptTool — 系统优化工具
体检仪表盘 : selected, alive=True
清理释放 : selected, alive=True
优化调整 : selected, alive=True
游戏环境 : selected, alive=True
撤销中心 : selected, alive=True
报告 : selected, alive=True
设置 : selected, alive=True
Final alive: True
Process stopped by test.
```

即：启动 10 秒后进程存活、主窗口标题为「OptTool — 系统优化工具」、7 个导航项逐个切换均不崩溃。

### 3.5 禁止项自查 → 输出为空
命令：

```powershell
Select-String -Path (Get-ChildItem G:\cft\src\OptTool -Recurse -Include *.cs).FullName -Pattern 'System\.Management|powershell\.exe|wmic|Assembly\.Location'
```

输出：`EMPTY: no forbidden patterns`（无任何命中）。
说明：本轮发现两处 **注释文字** 中含「Assembly.Location」字样（既有 `BackupService.cs` 注释与新代码注释），已把注释改写为「禁止用程序集路径定位」，含义不变、未改任何 API，复查为空。

### 3.6 报告导出实测（UI 自动化点击三个导出按钮）
脚本：`G:\cft\reports\ui-export-test.ps1`。输出：

```
导出 HTML clicked, alive=True
导出 TXT clicked, alive=True
导出 JSON clicked, alive=True
--- files in G:\cft\dist\data\reports ---
OptTool-report-20261006-214457.html  2826 bytes
OptTool-report-20261006-214459.txt  1171 bytes
OptTool-report-20261006-214501.json  2160 bytes
HTML has <html>: True
HTML has inline style: True
JSON parses, Os = Windows 11, Cpu = AMD Ryzen 9 8940HX with Radeon Graphics（x64，标称 2395 MHz）
done
```

TXT 报告真实内容节选：

```
操作系统：Windows 11（版本 26H2）
内部版本：26300.9457（CoreCountrySpecific）
处理器  ：AMD Ryzen 9 8940HX with Radeon Graphics（x64，标称 2395 MHz）
逻辑核心：16
内存    ：13.98 GB / 15.22 GB（占用 91%）
磁盘卷：C: [OS] … H: []（共 7 卷，数值真实）
图形适配器：
  [硬件] NVIDIA GeForce RTX 5070 Laptop GPU，7.71 GB 显存（0x10DE:0x2D58）
  [硬件] AMD Radeon(TM) 610M，485.78 MB 显存（0x1002:0x164E）
  [软件] Microsoft Basic Render Driver（0x1414:0x008C）
D3D12 特性等级：RTX 5070 = 12_2；AMD 610M = 12_1
硬件加速 GPU 调度：已启用
提权状态：普通权限（…需以管理员身份重新启动）
```

---

## 4. 遗留问题 / 未实现部分（如实列出）

1. **所有修改类动作均未执行**（按本任务要求）：清理「执行清理」、优化「修改」、撤销「撤销此操作」均只弹「第一版暂不…」提示，不做删除/注册表改动/回滚。
2. Game 页按任务要求只做只读体检，**未提供任何可逆开关或评分卡**；ARCHITECTURE.md §3.1 中的「游戏环境评分卡 + 可逆开关」留待后续版本。
3. Optimize 页本轮只实现「启动项」部分；§3.1 提到的服务、电源计划、视觉效果、隐私开关未做（任务 2 只点名启动项）。
4. 回收站大小为全部驱动器汇总值，未按驱动器分别显示（`SHQueryRecycleBin` 支持按盘查询，后续可扩展）。
5. 驱动重复条目只在 ViewModel 展示层去重；`GpuInfoService.GetDriverInfo()` 仍会返回重复记录（为保持公开 API 与既有测试不变，未在服务层改动）。
6. 「Microsoft Remote Display Adapter」驱动日期 2006-06-21 为系统虚拟显示的占位值，会触发「超过 365 天」提示；该提示对真实物理显卡（RTX 19 天 / AMD 47 天）不成立，后续可对虚拟/远程显示适配器豁免该警告。
7. 本次未做页面截图比对，运行验证以「进程存活 + UIA 窗口标题 + UIA 文本抓取」为准（各页面真实文本已抓取确认，脚本存于 `G:\cft\reports\ui-*.ps1`）。
8. Windows.old 提示逻辑已实现，但本机当前不存在该目录，未产生实际提示截图。

---

## 5. 结论

- 导航同步缺陷已修复并经 UI 自动化验证：启动后侧栏高亮与内容区一致，7 项切换不崩溃。
- 6 个原占位页面全部改为真实数据内容，无假数据、无「待实现」字样；报告 HTML/TXT/JSON 导出经实际点击验证可用。
- build 0 错误 0 警告、41 个既有测试全部通过、自包含 publish 成功且 `OptTool.pri` 存在、禁止项自查为空。
- 未越界：未执行任何真实系统清理/优化，未新增第三方 NuGet 包，未改动 DashboardPage 与既有 Services 的公开 API。
