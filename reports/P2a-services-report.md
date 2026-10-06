# P2a 报告：OptTool Services 地基层 + 单元测试

## 1. 实现清单

### 1.1 Models / SystemInfoModels.cs
定义 6 个 record：`CpuInfo`、`MemoryInfo`、`OsInfo`、`DiskInfo`、`StartupEntry`、`ElevationStatus`。全部不可变，字段与任务要求一一对应。

### 1.2 Interop / NativeMethods.cs
Win32 P/Invoke 声明（只读查询）：
- `GetSystemInfo`（kernel32）+ `SYSTEM_INFO` 结构体 + 处理器架构常量。
- `GlobalMemoryStatusEx`（kernel32）+ `MEMORYSTATUSEX` 结构体。
全部使用 Unicode 变体（这两个 API 无 A/W 之分），无 WMI / PowerShell。

### 1.3 Services / RegistryKeyParser.cs
内部辅助：把 `HKLM\...` / `HKEY_LOCAL_MACHINE\...` 形式的路径解析为 `(RegistryKey root, string subKey)`，支持 HKLM/HKCU/HKCR/HKU/HKCC。供 BackupService 与 OperationHistory 共用。

### 1.4 Services / RegistryValueCodec.cs
内部辅助：注册表值的可序列化表示。
- `Serialize(object?, RegistryValueKind) → string?`：String/DWord/QWord/MultiString/Binary/None 各自序列化为可写 JSON 的字符串（MultiString 用 JSON 数组，Binary 用 Base64）。
- `Deserialize(string?, string?) → (object? Value, RegistryValueKind Kind)`：反序列化为可写回注册表的对象与类型。
不使用 reg export，全部通过 Registry API 逐值读取后自行序列化。

### 1.5 Services / SystemInfoService.cs
6 个方法，全部独立 try/catch，单项失败返回"不可用"占位，不抛异常；普通权限下全部安全（只读）。
- `GetCpu()`：`GetSystemInfo` 取架构/逻辑处理器数/页大小；注册表 `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0` 取 `ProcessorNameString` 与 `~MHz`。
- `GetMemory()`：`GlobalMemoryStatusEx` 取总量/可用量/占用率。
- `GetOs()`：注册表 `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion` 取 ProductName/DisplayVersion/CurrentBuildNumber/UBR/EditionID。**特殊处理**：`CurrentBuildNumber >= 22000` 判定为 Windows 11 并把产品名覆盖为 "Windows 11"（不直接照抄 ProductName，因为 Win11 上该值仍可能写 "Windows 10"）。
- `GetDisks()`：`DriveInfo.GetDrives()`，跳过未就绪卷，单卷失败不影响整体。
- `GetStartupEntries()`：HKCU/HKLM 的 `...\CurrentVersion\Run` 键 + 当前用户与公共启动文件夹。
- `GetElevation()`：`WindowsIdentity.GetCurrent()` + `WindowsPrincipal.IsInRole(Administrator)`，未提权时 `CanWriteSystemSettings=false` 并给出原因。

### 1.6 Services / GuardList.cs
静态硬编码禁止清单，4 个方法均返回 `(bool Allowed, string Reason)`：
- `IsPathAllowed`：禁止桌面、文档、下载、图片、视频、OneDrive（用 `Environment.GetFolderPath` 取真实路径，OneDrive 优先读 `OneDrive` 环境变量）。用 `Path.GetFullPath` 归一化后做前缀匹配（带目录分隔符，避免 `Documents2` 误命中）。
- `IsServiceAllowed`：禁止 RpcSs、DcomLaunch、Dhcp、Dnscache、AudioSrv、WinDefend、BFE、ProfSvc、Themes（忽略大小写）。
- `IsRegistryKeyAllowed`：禁止路径含 Winlogon、Image File Execution Options、SafeBoot、BootExecute（子串匹配，忽略大小写）。
- `IsOperationAllowed`：`service:wuauserv:disable` 拒绝、`service:wuauserv:pause` 允许；命名操作 disable-windows-update / disable-defender / registry-cleanup 拒绝。
所有规则写死在代码里，不可通过参数绕过。

### 1.7 Services / BackupService.cs
- `record RegistryBackupResult(bool Success, string? BackupPath, string? Error)`。
- `BackupRegistryValue(keyPath, valueName)`：逐值读取，快照记录 `exists` / `kind` / `value`，序列化为 JSON 写入 `data\backup\`。即使值不存在也生成 exists=false 快照（供回滚时删除）。
- `Restore(backupPath)`：注册表快照 → 存在则写回原值与原类型，不存在则删除该值（`DeleteValue(throwOnMissingValue:false)`，不是写成空值）；文件备份 → 读 sidecar `.meta.json` 移回原路径。
- `BackupFile(filePath)`：移动到 `data\rescue\<时间戳>\`，写 sidecar 记录原路径，返回新路径。
- 数据目录默认 `AppContext.BaseDirectory\data`（未使用 `Assembly.Location`），构造函数支持注入以便单元测试隔离。

### 1.8 Services / OperationHistory.cs
- `record ValueChange`（Target/ValueName/OriginalExists/OriginalValue/OriginalKind/NewValue）。
- `record OperationRecord`（SchemaVersion 固定 1 / Id / Timestamp / OperationName / RiskLevel / Changes）。
- `Append(record)`：追加写入 `data\history.jsonl`（每行一条 JSON）。
- `ReadAll()`：逐行反序列化，损坏行跳过。
- `Rollback(operationId)`：按 Id 找到记录，逆向遍历 Changes；原本存在的值写回原值与原类型，原本不存在的值删除。全部成功返回 true，任一失败返回 false。
- 数据目录默认 `AppContext.BaseDirectory\data`，构造函数支持注入。

### 1.9 测试工程 OptTool.Tests
- `OptTool.Tests.csproj`：目标 `net10.0-windows10.0.26100.0`，引用 xunit 2.9.2 / xunit.runner.visualstudio 2.8.2 / Microsoft.NET.Test.Sdk 18.10.1（均已在 G:\nuget-cache 缓存）。
- **采用链接源文件（`<Compile Include="..\OptTool\...">`）方式编译主工程的纯 C# 服务层源码，未 ProjectReference 主工程**。原因：主工程是 WinUI 3 可执行项目（`UseWinUI=true`、OutputType=WinExe），直接引用会把 Windows App SDK 构建目标（XAML 编译、Pri 生成、打包相关目标）传递进测试工程，增加构建复杂度与失败面；而被测的 Models/Services/Interop 全部是纯 C#，不依赖 `Microsoft.UI.Xaml`，链接编译即可。源码仍是单一事实来源，未复制。
- `TestUtils.cs`：`TempRegistryKey` 辅助类，在 `HKCU\Software\OptToolTest\<随机>` 下创建临时键，Dispose 时递归删除，保证测试之间互不污染。
- `GuardListTests.cs`：9 个用例。
- `BackupServiceTests.cs`：2 个用例。
- `OperationHistoryTests.cs`：3 个用例。

## 2. 单元测试清单（共 14 个，全部通过）

### GuardListTests（9）
| 用例 | 通过情况 |
|---|---|
| DesktopPath_IsRejected | 通过 |
| OneDrivePath_IsRejected | 通过 |
| RpcSsService_IsRejected | 通过 |
| WinDefendService_IsRejected | 通过 |
| WuauservDisable_IsRejected | 通过 |
| WuauservPause_IsAllowed | 通过 |
| BootExecuteRegistryKey_IsRejected | 通过 |
| TempPath_IsAllowed | 通过 |
| WindowsTempPath_IsAllowed | 通过 |

### BackupServiceTests（2）
| 用例 | 通过情况 |
|---|---|
| BackupAndRestore_RegistryValue_PreservesValueAndKind | 通过 |
| Restore_BackupOfNonExistentValue_DeletesValue | 通过 |

### OperationHistoryTests（3）
| 用例 | 通过情况 |
|---|---|
| Append_ThenReadAll_FieldsMatch | 通过 |
| Rollback_NewValueRecord_DeletesValue | 通过 |
| Rollback_ChangedValueRecord_RestoresOriginal | 通过 |

测试运行结果：`已通过! - 失败: 0，通过: 14，已跳过: 0，总计: 14，持续时间: 111 ms`。

## 3. 验证记录

### 3.1 dotnet build 主工程（必须 0 错误）

命令（在 `G:\cft\src\OptTool` 下，`NUGET_PACKAGES=G:\nuget-cache`）：

```powershell
dotnet build
```

真实输出（末尾）：

```
正在确定要还原的项目…
  已还原 G:\cft\src\OptTool\OptTool.csproj (用时 1.84 秒)。
  OptTool -> G:\cft\src\OptTool\bin\Debug\net10.0-windows10.0.26100.0\win-x64\OptTool.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:16.27
```

结果：0 警告 0 错误，通过。

### 3.2 dotnet test（必须全部通过）

命令（在 `G:\cft\src\OptTool.Tests` 下）：

```powershell
dotnet test
```

真实输出（末尾）：

```
正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  OptTool.Tests -> G:\cft\src\OptTool.Tests\bin\Debug\net10.0-windows10.0.26100.0\OptTool.Tests.dll
G:\cft\src\OptTool.Tests\bin\Debug\net10.0-windows10.0.26100.0\OptTool.Tests.dll (.NETCoreApp,Version=v10.0)的测试运行
总共 1 个测试文件与指定模式相匹配。

已通过! - 失败:     0，通过:    14，已跳过:     0，总计:    14，持续时间: 111 ms - OptTool.Tests.dll (net10.0)
```

结果：14 通过 0 失败，通过。

> 过程记录：首次 `dotnet test` 编译失败，报错 `CS0246 未能找到类型或命名空间名 Fact`，根因是三个测试文件缺少 `using Xunit;`（xunit 不会随 ImplicitUsings 自动导入）。补上 `using Xunit;` 后重跑，全部通过。这是测试工程的问题，不影响主工程代码。

### 3.3 禁止项自查（输出必须为空）

命令（在 `G:\cft\src\OptTool` 下）：

```powershell
Select-String -Path (Get-ChildItem -Recurse -Include *.cs).FullName -Pattern 'System\.Management|ManagementObjectSearcher|powershell\.exe|pwsh\.exe|wmic'
```

真实输出：

```
forbidden-match-count=0
```

结果：0 命中，通过。无 `System.Management`、`ManagementObjectSearcher`、`powershell.exe`、`pwsh.exe`、`wmic`。

### 3.4 dotnet publish 仍然成功，且 dist\OptTool.pri 存在

命令（在 `G:\cft\src\OptTool` 下，先删 dist）：

```powershell
Remove-Item G:\cft\dist -Recurse -Force
dotnet publish -c Release -o G:\cft\dist
```

真实输出（末尾）：

```
正在确定要还原的项目…
  已还原 G:\cft\src\OptTool\OptTool.csproj (用时 349 毫秒)。
  OptTool -> G:\cft\src\OptTool\bin\Release\net10.0-windows10.0.26100.0\win-x64\OptTool.dll
  OptTool -> G:\cft\dist\
---
OptTool.pri exists: True
2196296
```

结果：publish 成功，`G:\cft\dist\OptTool.pri` 存在，大小 2,196,296 字节（由 P1.5 加入的 `CopyPriToPublish` 目标自动复制，非手工）。通过。

## 4. 遇到的技术难点与解决方式

1. **测试工程如何引用 WinUI 主工程**：主工程是 WinUI 3 可执行项目，直接 `ProjectReference` 会传递 Windows App SDK 构建目标。解决：测试工程以 `<Compile Include="..\OptTool\...">` 链接方式编译纯 C# 服务层源码，目标框架设为 `net10.0-windows10.0.26100.0` 以获得 Registry / WindowsIdentity 等 API。源码单一事实来源，未复制。
2. **测试文件首次编译缺 `using Xunit;`**：xunit 的 `[Fact]` / `Assert` 在 `Xunit` 命名空间，不会随 `ImplicitUsings` 自动导入。补上后通过。
3. **注册表值序列化的类型保真**：MultiString（string[]）与 Binary（byte[]）不能直接 `ToString()`。解决：MultiString 序列化为 JSON 数组字符串，Binary 序列化为 Base64，反序列化时按 `OriginalKind` 还原，保证备份→还原后值与类型完全一致（由 BackupServiceTests 第一条用例验证）。
4. **"原本不存在的值回滚后必须消失"**：不能把值写成空字符串或 null，必须调用 `RegistryKey.DeleteValue(throwOnMissingValue:false)`。BackupService.Restore 与 OperationHistory.Rollback 均按此实现（分别由 BackupServiceTests 第二条与 OperationHistoryTests 第二条验证）。
5. **OS 版本 ProductName 在 Win11 上不可靠**：按任务要求用 `CurrentBuildNumber >= 22000` 判定并覆盖产品名为 "Windows 11"。

## 5. 遗留问题 / 未验证部分

- `SystemInfoService` 的 6 个方法未写单元测试：它们读取真实系统状态（CPU/内存/OS/磁盘/启动项/提权），属于集成性质，本次任务的测试要求聚焦在 GuardList / BackupService / OperationHistory。方法内部全部 try/catch，普通权限下已通过主工程编译；实际运行时的返回值正确性未在本阶段自动化验证（后续界面接入时可做集成验证）。
- `BackupService.BackupFile` 的文件备份/还原路径未写专门单元测试（本次测试要求的 2 个 BackupService 用例覆盖注册表备份/还原）。代码已实现并编译通过，sidecar 机制与 Restore 的文件分支已就绪。
- 未真实执行任何系统清理/优化命令（遵守任务边界），GuardList 的拦截仅通过单元测试验证路径/服务/注册表/操作四个维度。
- 无其他遗留问题。
