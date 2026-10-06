# OptTool 架构与实现契约

> 本文件是**实现契约**，由 Lead 编写、由实现方（豆包）遵守。
> 派发任务时必须以本文件为准；与本文件冲突的实现一律视为不合格。

---

## 1. 项目定位

一个 Windows 系统优化工具。**WinUI 3 桌面应用**，不再追求轻量。

- 方案目录：`G:\cft\src\OptTool`
- 目标框架：`net10.0-windows10.0.26100.0`
- UI 框架：**WinUI 3**（Windows App SDK）
- 语言：**C#**，XAML 做视图

---

## 2. 部署模式（**必须改，当前配置是错的**）

当前模板产出的是 **MSIX 打包式**应用。要求改为 **非打包（unpackaged）+ 自包含（self-contained）**。

必须完成的改动：

- [ ] `<EnableMsixTooling>false</EnableMsixTooling>`（或直接移除）
- [ ] 移除 `Package.appxmanifest` 及 `Package.appxmanifest` 相关的 MSIX 配置
- [ ] 改为自包含部署：`<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` + `<SelfContained>true</SelfContained>`
- [ ] 保留 `app.manifest`（**注意：unpackaged 应用不能声明 `requireAdministrator`，否则无法启动**；权限改为运行时按需提权，见 §5）
- [ ] `<PublishTrimmed>false</PublishTrimmed>`：WinUI 3 的 XAML 依赖反射，**裁剪会导致运行时崩溃**，必须关闭
- [ ] `<PublishReadyToRun>` 可保留

**验收标准**：`dotnet build` 与 `dotnet publish` 均成功，`dotnet run` 能弹出窗口。

---

## 3. 功能范围

### 3.1 导航结构（NavigationView 左侧菜单）

| 路由 | 页面 | 内容 |
|---|---|---|
| `dashboard` | 体检仪表盘 | 四张状态卡（系统 / 磁盘 / 启动项 / 可清理空间）+ 问题清单 + 「一键优化」按钮 |
| `clean`` | 清理释放 | 内置清理项列表，每项显示可释放空间、勾选框 |
| `optimize` | 优化调整 | 启动项、服务、电源计划、视觉效果、隐私开关 |
| `game` | 游戏环境 | 游戏环境评分卡 + 可逆开关 |
| `undo` | 撤销中心 | 操作历史列表 + 逐条撤销 |
| `report` | 报告 | 体检报告导出（HTML / TXT / JSON） |
| `settings` | 设置 | 备份目录、体积上限、关于 |

### 3.2 功能分级（必须按此分级控制 UI 呈现）

- 🟢 **只读**：直接可用，不需要确认
- 🟡 **低风险可逆**：默认勾选，一键优化会执行
- 🟠 **中风险**：默认不勾，执行前需确认对话框
- 🔴 **高风险**：**第一版不实现**（虚拟内存、注册表批量优化、暂停更新等，代码里不要写）

---

## 4. 硬性技术约束（**违反即不合格**）

### 4.1 禁止使用 WMI / CIM

禁止 `System.Management`、`ManagementObjectSearcher`、`Win32_*` 查询。

理由：WMI 依赖 COM 互操作，且在自包含部署下易出问题；更重要的是"应用静默查询 WMI"是杀软启发式的观察对象。

**改用原生 API 与注册表**：

| 需求 | 正确实现 |
|---|---|
| CPU 信息 | `GetSystemInfo` / `GetLogicalProcessorInformationEx` |
| 内存 | `GlobalMemoryStatusEx` |
| CPU 型号/频率 | 注册表 `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0` |
| 系统版本 | 注册表 `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion` |
| 主板/固件 | `GetSystemFirmwareTable`（读 SMBIOS） |
| 磁盘类型 SSD/HDD | `DeviceIoControl` 的 seek penalty 属性（**不要靠型号字符串猜**） |
| 启动项 | 注册表 `Run` 键 + 启动文件夹 |
| 进程列表 | `Process.GetProcesses()`（.NET API，允许） |

### 4.2 禁止调用 PowerShell 与 wmic

禁止 `powershell.exe`、`pwsh.exe`、`wmic.exe`、`-EncodedCommand`。

理由：无签名应用调用编码 PowerShell 是杀软最高置信度的恶意特征。

### 4.3 编码要求

- 所有 Win32 互操作使用 **Unicode（W）变体**，不要用 ANSI（A）变体
- 路径与字符串比较使用 `StringComparison.OrdinalIgnoreCase`
- 不要解析命令行输出的中文关键字（本地化输出不可靠），优先用 API 返回值

### 4.4 已知陷阱（务必避免）

- **不要用 `Assembly.Location`** 定位程序目录 —— 改用 `AppContext.BaseDirectory` 或 `Environment.ProcessPath`
- **不要启用 `InvariantGlobalization`**
- WinUI 3 **不要开启裁剪**（反射依赖）

---

## 5. 权限模型

- 应用以**普通权限**启动（unpackaged + requireAdministrator 组合无法启动）
- 需要写入系统设置的操作，**执行前检测是否已提权**：
  - 已提权 → 直接执行
  - 未提权 → 弹出说明，引导用户"以管理员身份重新启动"
- 只读功能（体检仪表盘）在普通权限下必须完全可用

---

## 6. 安全底座（**核心要求，不可省略**）

**每个会修改系统的操作，必须走五步闭环**：

```
检测 → 备份 → 执行 → 复验 → 可回滚
```

具体要求：

- [ ] **执行前备份**：注册表值导出、服务原启动类型、启动项原值 → 存到数据目录
- [ ] **操作历史**：`history.json` 记录 `时间 / 操作项 / 改前值 / 改后值 / 回滚信息`
- [ ] **失败自动回滚**：任何一步失败，必须恢复该项的原状态，不能留半成品
- [ ] **变更预览**：执行前展示"将修改哪些键/文件/服务"
- [ ] **数据目录**：`AppContext.BaseDirectory\data\`，含 `backup\`、`logs\`、`history.json`
- [ ] **exe 目录不可写时**：明确提示并提供回退目录，**不得静默失败**

### 6.1 硬编码禁止清单（写死在代码里，不可通过 UI 绕过）

**禁止触碰的路径**：桌面、文档、下载、图片、视频、OneDrive

**禁止修改的服务**：`RpcSs`、`DcomLaunch`、`Dhcp`、`Dnscache`、`AudioSrv`、`WinDefend`、`BFE`、`ProfSvc`、`Themes`
（`wuauserv` 只允许"暂停"，永不允许"禁用"）

**禁止触碰的注册表**：`Winlogon\Shell`、`Image File Execution Options`、`SafeBoot`、`BootExecute`

**禁止实现的功能**：
- ❌ 永久关闭 Windows 更新
- ❌ 关闭 Defender / 卸载安全软件
- ❌ 第三方式模糊"注册表清理"
- ❌ 任何静默捆绑、改主页、自启动

### 6.2 游戏模块专属禁止项

- ❌ 注入游戏进程、加载 DLL、覆盖游戏配置文件（会被反作弊判为作弊）
- ❌ `bcdedit` 任何修改（会破坏 BitLocker 与安全启动）
- ❌ 禁用 VBS / 内存完整性 / 核心隔离

---

## 7. 清理模块的真实后果（UI 文案必须诚实）

| 项 | 要求 |
|---|---|
| 临时目录 / 回收站 / 缩略图缓存 / DNS 缓存 | 可做 |
| `Prefetch` | **不做**（清了反而拖慢启动） |
| `Windows.old` | **只提示存在 + 引导到系统设置，不代删** |
| WinSxS | **只调用官方 `DISM /StartComponentCleanup`**；**禁止 `/ResetBase`** |
| `SoftwareDistribution\Download` | 需先停 `wuauserv`/`BITS`，**必须记录并恢复这两个服务的原始状态** |
| `cleanmgr` | **不调用**（异步，无法统计结果） |
| 占用中的文件 | **跳过并计数，不报错中断** |

---

## 8. 代码组织要求

```
src/OptTool/
├── App.xaml / App.xaml.cs           # 应用入口
├── MainWindow.xaml / .cs            # 主窗口（NavigationView 外壳）
├── Pages/                           # 各功能页面（见 §3.1）
├── ViewModels/                      # MVVM，页面逻辑
├── Services/                        # 业务服务
│   ├── SystemInfoService.cs         # 系统信息采集（原生 API）
│   ├── CleanupService.cs            # 清理
│   ├── OptimizeService.cs           # 优化
│   ├── GameEnvService.cs            # 游戏环境
│   ├── BackupService.cs             # 备份
│   ├── HistoryService.cs            # 操作历史与回滚
│   └── ReportService.cs             # 报告导出
├── Interop/                         # P/Invoke 声明
└── Models/                          # 数据模型
```

**要求**：
- 用 **MVVM**，不要在 code-behind 里堆业务逻辑
- 所有系统调用包 `try/catch`，**单项失败不影响整体**
- 每个服务方法都要能在无管理员权限时安全返回"需要提权"状态，而不是崩

---

## 9. 交付验收标准

实现方必须保证：

- [ ] `dotnet build` 零错误
- [ ] `dotnet publish -c Release` 成功产出可运行程序
- [ ] 程序能启动，NavigationView 各页面能正常切换
- [ ] 体检仪表盘能显示真实的 CPU / 内存 / 系统信息（不是假数据）
- [ ] 未提权时可正常启动并完成只读体检

**Lead 会独立验证**：重新构建、实际运行、检查是否有 WMI/PowerShell 调用、检查五步闭环是否真实存在、检查禁止清单是否被绕过。

---

## 10. 与 Lead 的沟通要求

- 每完成一个阶段，用状态回报命令报告（`status.mjs start/progress/done/fail`）
- **只回复"已完成"不算数**，必须有实际文件产出
- 遇到不确定的架构决策，**停下来问**，不要自己猜
- 发现本契约有矛盾或不可行之处，**直接指出**，不要默默绕过
