# OptTool — Windows 系统优化工具

轻量、可回滚的 Windows 系统优化工具。**当前处于第一版 demo 阶段。**

## 当前状态

| 模块 | 状态 |
|---|---|
| 体检仪表盘（真实数据） | ✅ 可用 |
| Services 层（系统信息 / 显卡 / 备份 / 历史） | ✅ 41 个单元测试全部通过 |
| 导航（7 个页面） | ✅ 已修复同步缺陷 |
| 清理 / 优化 / 游戏环境 / 撤销 / 报告 / 设置 页面 | ⚠️ **代码已实现但存在显示缺陷**（见下方"已知问题"） |

### 已验证的实测结果

- 单次体检显示：Windows 11 / AMD Ryzen 9 8940HX / 内存 85%（13.1 GB / 15.2 GB）
- 显卡枚举：NVIDIA RTX 5070 Laptop + AMD Radeon 610M（2 个硬件适配器）+ Microsoft Basic Render Driver（正确标注为软件适配器）
- DirectX 特性等级：**12_2**；硬件加速 GPU 调度（HAGS）：已启用
- 构建：0 错误 0 警告；单元测试 41/41 通过

## 已知问题（下一步待修）

**6 个页面会卡在"加载中"**，两个根因已定位：

1. **x:Bind 通知键不匹配**：`{x:Bind ViewModel.X}` 使绑定监听 `"ViewModel.X"`，
   而 ViewModel 发出的通知键是 `"X"`，界面永不更新。
   → 修法：把被绑定属性搬到 Page 自身并实现 `INotifyPropertyChanged`，绑定改为 `{x:Bind X}`。
2. **后台线程更新绑定属性抛 COMException**：采集在后台线程时，属性赋值必须经
   `DispatcherQueue.TryEnqueue` 切回 UI 线程。

`DashboardPage` 已按上述方式修好，可作为范例。

## 技术选型

| 项 | 选择 | 说明 |
|---|---|---|
| UI 框架 | **WinUI 3**（Windows App SDK 2.5.1） | 非打包（unpackaged）+ 自包含（self-contained） |
| 目标框架 | `net10.0-windows10.0.26100.0` | x64 |
| 系统信息采集 | **原生 API + 注册表** | 刻意不用 WMI/CIM，也不调 PowerShell |
| 显卡枚举 | **DXGI**（原生 vtable 函数指针调用 COM） | 见 `Interop/DxgiInterop.cs` |
| 测试 | xUnit（链接源码方式，单一事实来源） | `src/OptTool.Tests` |

### 为什么不使用 WMI / PowerShell

- WMI 依赖 COM 互操作，且"应用静默查询 WMI"是杀软启发式的观察对象
- 无签名应用调用 `powershell -EncodedCommand` 是最高置信度的恶意特征

## 构建

```powershell
# 前置条件
#   - .NET SDK 10
#   - MSVC 链接器（VS 生成工具的 C++ 桌面开发工作负载）
#   - Windows SDK 完整 um 库（缺 advapi32.lib 会链接失败）

dotnet build src\OptTool\OptTool.csproj
dotnet test  src\OptTool.Tests
dotnet publish src\OptTool\OptTool.csproj -c Release -o dist
```

> ⚠️ **发布后必须确认 `dist\OptTool.pri` 存在**：WinUI 3 应用自己的 PRI 资源缺失时
> 构建仍会成功，但程序启动即崩（`0xC000027B`）。`OptTool.csproj` 里的
> `CopyPriToPublish` 目标负责复制，并在缺失时让构建失败。

## 项目结构

```
.
├── ARCHITECTURE.md              实现契约（功能范围、安全约束、禁止清单）
├── reports/                     各阶段交付报告（含验证记录）
├── src/
│   ├── OptTool/
│   │   ├── App.xaml(.cs)        应用入口
│   │   ├── MainWindow.xaml(.cs) NavigationView 外壳 + 路由表
│   │   ├── Pages/               7 个功能页面
│   │   ├── ViewModels/          视图模型
│   │   ├── Services/            业务服务
│   │   │   ├── SystemInfoService.cs   系统信息（原生 API）
│   │   │   ├── GpuInfoService.cs      显卡与 DirectX 体检
│   │   │   ├── GuardList.cs           硬编码禁止清单
│   │   │   ├── BackupService.cs       注册表值 / 文件备份
│   │   │   └── OperationHistory.cs    操作历史与回滚
│   │   ├── Interop/             P/Invoke 与 DXGI 互操作
│   │   └── Models/              数据模型
│   └── OptTool.Tests/           xUnit 测试（链接源码）
└── .gitignore
```

## 安全设计

本工具的定位是**只读体检优先，所有修改可回滚**。

### 五步闭环

每个会修改系统的操作必须走：**检测 → 备份 → 执行 → 复验 → 可回滚**

- 备份记录值的**原始类型**与"原本是否存在"，回滚时执行逆操作而非导入备份
- 失败即中止并回滚当前项，不留半成品
- 备份目录按**体积上限**清理（默认 200 MB），未撤销项不因过期被删

### 硬编码禁止清单（`Services/GuardList.cs`）

- **路径**：桌面、文档、下载、图片、视频、OneDrive
- **服务**：`RpcSs`、`DcomLaunch`、`Dhcp`、`Dnscache`、`AudioSrv`、`WinDefend`、`BFE`、`ProfSvc`、`Themes`
  （`wuauserv` 只允许暂停，不允许禁用）
- **注册表**：`Winlogon\Shell`、`Image File Execution Options`、`SafeBoot`、`BootExecute`

### 明确不实现

- ❌ 永久关闭 Windows 更新
- ❌ 关闭 Defender / 卸载安全软件
- ❌ 第三方"注册表清理器"式模糊删除
- ❌ 注入游戏进程 / 修改游戏配置文件（会被反作弊判为作弊）
- ❌ `bcdedit` 任何修改（会破坏 BitLocker 与安全启动）
- ❌ 禁用 VBS / 内存完整性 / 核心隔离
- ❌ 任何静默捆绑、改主页、静默自启动

## 隐私声明

- **不联网**：程序不发起任何网络连接
- **不上传数据**：所有检测结果与备份只保存在本机
- **不做静默修改**：任何系统改动前都会明确提示并等待确认
- **所有改动可回滚**：执行前先备份，可在「撤销中心」还原

## 开发方式说明

本项目由 AI 协作开发：豆包负责界面与功能的实现，Lead 负责架构决策、独立验证与 Bug 定位。
`reports/` 下的每份报告都包含**真实命令与真实输出**的验证记录。

## 许可

尚未确定。
