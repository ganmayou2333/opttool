namespace OptTool.Services;

/// <summary>
/// 硬编码禁止清单。所有规则写死在代码里，不可通过参数绕过。
/// 用于在执行任何系统修改前做前置拦截。
/// </summary>
public static class GuardList
{
    // ---- 禁止触碰的路径（用 Environment.GetFolderPath 取真实路径） ----
    private static readonly string[] ForbiddenPaths = BuildForbiddenPaths();

    private static string[] BuildForbiddenPaths()
    {
        var list = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        };

        // OneDrive：优先用环境变量，其次用用户目录下的 OneDrive 文件夹。
        string? oneDriveEnv = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrWhiteSpace(oneDriveEnv)) list.Add(oneDriveEnv);
        list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive"));

        return list
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // ---- 禁止修改的服务 ----
    private static readonly HashSet<string> ForbiddenServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "DcomLaunch", "Dhcp", "Dnscache", "AudioSrv",
        "WinDefend", "BFE", "ProfSvc", "Themes",
    };

    // ---- 禁止触碰的注册表键（完整路径段匹配，忽略大小写） ----
    // Winlogon\Shell 必须是连续的两个路径段（winlogon 后紧跟 shell）；
    // 其余三项按单个完整路径段匹配。
    private const string WinlogonSegment = "winlogon";
    private const string ShellSegment = "shell";
    private static readonly string[] ForbiddenRegistrySegments = new[]
    {
        "image file execution options",
        "safeboot",
        "bootexecute",
    };

    // ---- 禁止实现的操作 ----
    private static readonly string[] ForbiddenOperationIds = new[]
    {
        "disable-windows-update",
        "disable-defender",
        "registry-cleanup",
    };

    /// <summary>
    /// 路径是否允许操作。返回 (allowed, reason)。
    /// 先做真实路径解析（符号链接 / junction），再与禁止目录比较。
    /// </summary>
    public static (bool Allowed, string Reason) IsPathAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (false, "路径为空");

        string resolved = ResolveRealPath(path);
        string normalized = resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var forbidden in ForbiddenPaths)
        {
            if (normalized.Equals(forbidden, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(forbidden + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"路径位于受保护目录下: {forbidden}");
            }
        }
        return (true, string.Empty);
    }

    /// <summary>
    /// 真实路径解析：优先用 File/Directory.ResolveLinkTarget(returnFinalTarget: true)
    /// 解析符号链接与 junction；路径不存在时退回 Path.GetFullPath。
    /// 任何异常都 catch 住，不抛出。
    /// </summary>
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
            return Path.GetFullPath(path);
        }
        catch
        {
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }
    }

    /// <summary>服务是否允许修改。返回 (allowed, reason)。</summary>
    public static (bool Allowed, string Reason) IsServiceAllowed(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return (false, "服务名为空");
        if (ForbiddenServices.Contains(serviceName))
            return (false, $"服务在禁止修改清单中: {serviceName}");
        return (true, string.Empty);
    }

    /// <summary>
    /// 注册表键是否允许操作。返回 (allowed, reason)。
    /// 使用完整路径段匹配（不是裸子串匹配），避免 ADMX_WinLogon、
    /// Windows.SystemToast.Winlogon 等合法键被误伤。
    /// 归一化：分隔符 / 与 \ 统一、忽略大小写、去掉结尾分隔符。
    /// </summary>
    public static (bool Allowed, string Reason) IsRegistryKeyAllowed(string keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
            return (false, "注册表路径为空");

        string normalized = keyPath.Replace('/', '\\').Trim('\\');
        var segments = normalized
            .Split('\\', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().ToLowerInvariant())
            .ToList();

        // Winlogon\Shell：必须是连续的两个路径段（winlogon 后紧跟 shell）。
        for (int i = 0; i < segments.Count - 1; i++)
        {
            if (segments[i] == WinlogonSegment && segments[i + 1] == ShellSegment)
                return (false, "注册表键在禁止触碰清单中: Winlogon\\Shell");
        }

        // 其余禁止项：单个完整路径段精确匹配。
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

    /// <summary>
    /// 操作是否允许。支持两种约定：
    ///   1) "service:&lt;name&gt;:&lt;action&gt;"——例如 service:wuauserv:disable（拒绝）、service:wuauserv:pause（允许）
    ///   2) 命名操作 id——disable-windows-update / disable-defender / registry-cleanup（拒绝）
    /// </summary>
    public static (bool Allowed, string Reason) IsOperationAllowed(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId))
            return (false, "操作 id 为空");

        string id = operationId.Trim();

        // wuauserv 只允许暂停，不允许禁用。
        if (id.StartsWith("service:wuauserv:", StringComparison.OrdinalIgnoreCase))
        {
            string action = id.Substring("service:wuauserv:".Length);
            if (action.Equals("disable", StringComparison.OrdinalIgnoreCase))
                return (false, "wuauserv 只允许暂停，不允许禁用");
            return (true, string.Empty);
        }

        foreach (var forbidden in ForbiddenOperationIds)
        {
            if (id.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                return (false, $"操作在禁止实现清单中: {forbidden}");
        }

        return (true, string.Empty);
    }
}
