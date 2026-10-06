# P2a-fix 报告：代码审查 3 个缺陷修复

## 1. 三个缺陷的修复前后对比

### 缺陷 1：GuardList 注册表匹配过宽，误伤合法操作

**文件**：`G:\cft\src\OptTool\Services\GuardList.cs`

**修复前**（裸子串匹配，`Contains`）：

```csharp
private static readonly string[] ForbiddenRegistryKeyParts = new[]
{
    "Winlogon",
    "Image File Execution Options",
    "SafeBoot",
    "BootExecute",
};

public static (bool Allowed, string Reason) IsRegistryKeyAllowed(string keyPath)
{
    // ...
    string normalized = keyPath.Replace('/', '\\');
    foreach (var part in ForbiddenRegistryKeyParts)
    {
        if (normalized.Contains(part, StringComparison.OrdinalIgnoreCase))
            return (false, $"注册表键在禁止触碰清单中: {part}");
    }
    return (true, string.Empty);
}
```

问题：`ADMX_WinLogon`、`...\Winlogon\PasswordExpiryNotification`、`Windows.SystemToast.Winlogon` 等合法键因含 "Winlogon" 子串被误拒。

**修复后**（完整路径段匹配）：

```csharp
private const string WinlogonSegment = "winlogon";
private const string ShellSegment = "shell";
private static readonly string[] ForbiddenRegistrySegments = new[]
{
    "image file execution options",
    "safeboot",
    "bootexecute",
};

public static (bool Allowed, string Reason) IsRegistryKeyAllowed(string keyPath)
{
    if (string.IsNullOrWhiteSpace(keyPath))
        return (false, "注册表路径为空");

    // 归一化：/ 与 \ 统一、忽略大小写、去掉结尾分隔符
    string normalized = keyPath.Replace('/', '\\').Trim('\\');
    var segments = normalized
        .Split('\\', StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Trim().ToLowerInvariant())
        .ToList();

    // Winlogon\Shell：必须是连续的两个路径段（winlogon 后紧跟 shell）
    for (int i = 0; i < segments.Count - 1; i++)
    {
        if (segments[i] == WinlogonSegment && segments[i + 1] == ShellSegment)
            return (false, "注册表键在禁止触碰清单中: Winlogon\\Shell");
    }

    // 其余禁止项：单个完整路径段精确匹配
    foreach (var seg in segments)
    {
        foreach (var forbidden in ForbiddenRegistrySegments)
        {
            if (seg == forbidden)
                return (false, $"注册表键在禁止触碰清单中: {forbidden}");
        }
    }
    return (true, string.Empty);
}
```

效果：`ADMX_WinLogon`（段名不等于 winlogon）、`...\Winlogon\PasswordExpiryNotification`（winlogon 后不是 shell）、`Windows.SystemToast.Winlogon`（段名不等于 winlogon）均被允许；只有真正的 `...\Winlogon\Shell` 连续段被拒绝。

---

### 缺陷 2：路径检查不解析符号链接 / junction，存在绕过通道

**文件**：`G:\cft\src\OptTool\Services\GuardList.cs`

**修复前**：

```csharp
public static (bool Allowed, string Reason) IsPathAllowed(string path)
{
    // ...
    string normalized = Path.GetFullPath(path).TrimEnd(...);  // 只做字面归一化
    // 与禁止目录比较
}
```

问题：用 junction / 符号链接指向受保护目录时，`Path.GetFullPath` 不解析链接，可绕过检查。

**修复后**（新增 `ResolveRealPath`，先解析再判断）：

```csharp
public static (bool Allowed, string Reason) IsPathAllowed(string path)
{
    if (string.IsNullOrWhiteSpace(path))
        return (false, "路径为空");

    string resolved = ResolveRealPath(path);   // 先做真实路径解析
    string normalized = resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    // ... 与禁止目录比较
}

private static string ResolveRealPath(string path)
{
    try
    {
        if (File.Exists(path))
        {
            var target = File.ResolveLinkTarget(path, returnFinalTarget: true);
            if (target != null) return target.FullName;
        }
        else if (Directory.Exists(path))
        {
            var target = Directory.ResolveLinkTarget(path, returnFinalTarget: true);
            if (target != null) return target.FullName;
        }
        return Path.GetFullPath(path);   // 路径不存在时退回
    }
    catch
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }            // 任何异常都不抛出
    }
}
```

说明：优先用 `File.ResolveLinkTarget` / `Directory.ResolveLinkTarget(returnFinalTarget: true)` 解析符号链接与 junction 的最终目标；路径不存在时退回 `Path.GetFullPath`；整个解析过程包 try/catch，不抛出。

---

### 缺陷 3：CopyPriToPublish 的 Condition 静默产出坏包

**文件**：`G:\cft\src\OptTool\OptTool.csproj`

**修复前**：

```xml
<Target Name="CopyPriToPublish" AfterTargets="Publish">
  <Copy SourceFiles="$(OutDir)$(TargetName).pri"
        DestinationFolder="$(PublishDir)"
        SkipUnchangedFiles="false"
        Condition="Exists('$(OutDir)$(TargetName).pri')" />
</Target>
```

问题：源 `.pri` 不存在时，`Condition` 让复制静默跳过，构建照样成功，但产出的程序启动即崩（`0xC000027B`）——这是 P1.5 故障的复发通道。

**修复后**：

```xml
<Target Name="CopyPriToPublish" AfterTargets="Publish">
  <Error Condition="!Exists('$(OutDir)$(TargetName).pri')"
         Text="未找到 $(OutDir)$(TargetName).pri：WinUI 3 应用的 PRI 资源文件缺失，若继续发布将产出一个启动即崩溃(0xC000027B)的坏包。请检查构建是否成功生成了 .pri 文件。" />
  <Copy SourceFiles="$(OutDir)$(TargetName).pri"
        DestinationFolder="$(PublishDir)"
        SkipUnchangedFiles="false" />
</Target>
```

效果：去掉了 `Condition`，先用 `<Error>` 在源 `.pri` 缺失时给出清晰错误并终止构建，再无条件执行 `Copy`。源缺失时构建直接失败（exit code 1），不再静默产出坏包。

## 2. 新增测试清单与通过情况

在 `G:\cft\src\OptTool.Tests\GuardListTests.cs` 新增 5 个回归测试（原有 14 个全部保留，未删除）：

| # | 用例名 | 验证点 | 通过情况 |
|---|---|---|---|
| 1 | AdmxWinLogonKey_IsAllowed | `HKLM\...\ADMX_WinLogon` 含 WinLogon 子串但非 Winlogon\Shell 段，不得误拒 | 通过 |
| 2 | WinlogonPasswordExpiryNotificationKey_IsAllowed | `HKCU\...\Winlogon\PasswordExpiryNotification` 是 Winlogon 下合法子键，不得误拒 | 通过 |
| 3 | WinlogonShellKey_IsRejected | 真正的 `...\Winlogon\Shell` 连续段必须拒绝；同时用正斜杠+结尾反斜杠验证边界归一化 | 通过 |
| 4 | BootExecuteExactSegment_IsRejected | `BootExecute` 作为完整路径段出现时拒绝（精确段匹配） | 通过 |
| 5 | CurrentVersionRunKey_IsAllowed | 普通合法键 `HKCU\...\CurrentVersion\Run` 必须允许 | 通过 |

测试运行结果：`已通过! - 失败: 0，通过: 19，已跳过: 0，总计: 19，持续时间: 106 ms`。原有 14 个 + 新增 5 个 = 19 个，全部通过。

## 3. 验证记录

### 3.1 dotnet build 主工程（必须 0 错误）

命令（在 `G:\cft\src\OptTool` 下，`NUGET_PACKAGES=G:\nuget-cache`）：

```powershell
dotnet build
```

真实输出（末尾）：

```
正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  OptTool -> G:\cft\src\OptTool\bin\Debug\net10.0-windows10.0.26100.0\win-x64\OptTool.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:11.66
```

结果：0 警告 0 错误，通过。

### 3.2 dotnet test（必须全部通过，应 >= 19 个）

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

已通过! - 失败:     0，通过:    19，已跳过:     0，总计:    19，持续时间: 106 ms - OptTool.Tests.dll (net10.0)
```

结果：19 通过 0 失败，通过。

### 3.3 删除 dist 后 dotnet publish，成功且 dist\OptTool.pri 存在，OptTool.exe 能启动不崩

命令（在 `G:\cft\src\OptTool` 下）：

```powershell
Remove-Item G:\cft\dist -Recurse -Force
dotnet publish -c Release -o G:\cft\dist
```

真实输出（末尾）：

```
正在确定要还原的项目…
  已还原 G:\cft\src\OptTool\OptTool.csproj (用时 356 毫秒)。
  OptTool -> G:\cft\src\OptTool\bin\Release\net10.0-windows10.0.26100.0\win-x64\OptTool.dll
  OptTool -> G:\cft\dist\
---
OptTool.pri exists: True
OptTool.exe exists: True
```

启动验证（运行 10 秒）：

```powershell
$p = Start-Process -FilePath 'G:\cft\dist\OptTool.exe' -PassThru
Start-Sleep -Seconds 10
$p.Refresh()
# 检查 $p.HasExited
```

真实输出：

```
after 10s HasExited=False alive=True exitCode=
process stopped
```

结果：publish 成功，`dist\OptTool.pri` 存在，`OptTool.exe` 存在；启动 10 秒后进程仍存活（`HasExited=False`），未崩溃；随后已关闭。通过。

### 3.4 缺陷 3 兜底负向验证：改名 bin 里的 .pri，确认构建失败而非静默通过

命令（在 `G:\cft\src\OptTool` 下）：

```powershell
$pri = 'G:\cft\src\OptTool\bin\Release\net10.0-windows10.0.26100.0\win-x64\OptTool.pri'
Rename-Item -Path $pri -NewName 'OptTool.pri.disabled' -Force
dotnet publish -c Release -o 'G:\cft\dist' --no-build
# 预期：失败，报出我们写的 Error 文本
Rename-Item -Path "$pri.disabled" -NewName 'OptTool.pri' -Force   # 改回来
```

真实输出：

```
pri exists before rename: True
after rename pri exists: False, bak exists: True
--- running publish --no-build (expect failure) ---
OptTool -> G:\cft\dist\
G:\cft\src\OptTool\OptTool.csproj(59,5): error : 未找到 bin\Release\net10.0-windows10.0.26100.0\win-x64\OptTool.pri：WinUI 3 应用的 PRI 资源文件缺失，若继续发布将产出一个启动即崩溃(0xC000027B)的坏包。请检查构建是否成功生成了 .pri 文件。
publish exit code: 1
--- restoring ---
restored pri exists: True
```

结果：`.pri` 缺失时，构建在 `OptTool.csproj(59,5)` 处报出我们写的清晰错误文本，退出码 1，**没有静默通过**；验证完成后已把文件改回（`restored pri exists: True`）。通过。

> 说明：使用 `--no-build` 是为了避免构建步骤重新生成 `.pri`，从而精确触发 CopyPriToPublish 目标里的 Error 兜底。这正是该兜底要拦截的场景：构建产物缺失但发布流程仍在执行。

## 4. 遗留问题 / 未验证部分

- 缺陷 2 的路径真实解析使用了 `File.ResolveLinkTarget` / `Directory.ResolveLinkTarget(returnFinalTarget: true)`，能解析符号链接与 junction。8.3 短名（如 `PROGRA~1`）的解析需要额外 P/Invoke `GetLongPathName`，本次按任务 prescribed 方案未引入；当前 `Path.GetFullPath` 不会展开 8.3 短名。若后续需要完全封堵 8.3 短名绕过，可在 `ResolveRealPath` 中补充 `kernel32!GetLongPathName` 调用。
- 缺陷 1 的 `Winlogon\Shell` 保护针对的是键路径中出现连续 `winlogon` + `shell` 段的情况。实际 `Shell` 是 `Winlogon` 键下的一个**值**（不是子键），`IsRegistryKeyAllowed` 只接收键路径、不接收值名，因此对 `HKLM\...\Winlogon` 键本身（不带 `\Shell` 段）不会拒绝。若后续需要在值粒度上保护 `Winlogon!Shell`，需扩展 API 同时传入值名，或在调用方组合判断。本次按任务要求的"键路径段匹配"实现。
- 未真实执行任何系统清理/优化命令（遵守任务边界），GuardList 的拦截仅通过单元测试验证。
- 无其他遗留问题。
