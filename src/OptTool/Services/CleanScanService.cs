using OptTool.Interop;

namespace OptTool.Services;

/// <summary>目录扫描结果：总字节、文件数、跳过（占用/无权限）数。</summary>
public record DirectoryScanResult(long SizeBytes, long FileCount, long SkippedCount);

/// <summary>
/// 只读的可清理空间扫描服务。只统计大小，不删除任何文件。
/// 占用中或无权限访问的文件跳过并计数，不因单个文件失败中断。
/// 禁止 WMI / PowerShell；回收站大小用 shell32 只读 API 查询。
/// </summary>
public static class CleanScanService
{
    /// <summary>递归统计目录大小。访问失败的子目录整体跳过并计入 skipped。</summary>
    public static DirectoryScanResult MeasureDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return new DirectoryScanResult(0, 0, 0);
        }

        var (size, files, skipped) = Walk(path);
        return new DirectoryScanResult(size, files, skipped);
    }

    private static (long size, long files, long skipped) Walk(string path)
    {
        long size = 0, files = 0, skipped = 0;

        string[]? entries = null;
        try
        {
            entries = Directory.GetFiles(path);
        }
        catch
        {
            // 无权限列目录：按跳过一个目录处理，不中断整体扫描。
            skipped++;
        }

        if (entries is not null)
        {
            foreach (var file in entries)
            {
                try
                {
                    size += new FileInfo(file).Length;
                    files++;
                }
                catch
                {
                    // 占用中/无权限的单个文件跳过计数。
                    skipped++;
                }
            }
        }

        string[]? dirs = null;
        try
        {
            dirs = Directory.GetDirectories(path);
        }
        catch
        {
            skipped++;
        }

        if (dirs is not null)
        {
            foreach (var dir in dirs)
            {
                var (s, f, k) = Walk(dir);
                size += s;
                files += f;
                skipped += k;
            }
        }

        return (size, files, skipped);
    }

    /// <summary>查询全部驱动器回收站的总大小与项数。查询失败返回 (0, 0)。</summary>
    public static (long SizeBytes, long ItemCount) QueryRecycleBin()
    {
        try
        {
            var info = new NativeMethods.SHQUERYRBINFO
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>(),
            };

            int hr = NativeMethods.SHQueryRecycleBin(null, ref info);
            if (hr == 0)
            {
                return (info.i64Size, info.i64NumItems);
            }
        }
        catch
        {
            // 查询异常降级为 0，不抛出。
        }

        return (0, 0);
    }

    /// <summary>
    /// 缩略图与图标缓存：%LocalAppData%\Microsoft\Windows\Explorer 下的
    /// thumbcache_*.db / iconcache_*.db 文件。
    /// </summary>
    public static DirectoryScanResult MeasureThumbnailCache()
    {
        long size = 0, files = 0, skipped = 0;
        try
        {
            string explorerDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Explorer");

            if (Directory.Exists(explorerDir))
            {
                foreach (var file in Directory.GetFiles(explorerDir, "*.db"))
                {
                    string name = Path.GetFileName(file);
                    if (name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            size += new FileInfo(file).Length;
                            files++;
                        }
                        catch
                        {
                            skipped++;
                        }
                    }
                }
            }
        }
        catch
        {
            // 整体失败返回已收集部分。
        }

        return new DirectoryScanResult(size, files, skipped);
    }

    /// <summary>
    /// 崩溃转储：用户 %LocalAppData%\CrashDumps、C:\Windows\Minidump、
    /// C:\Windows\MEMORY.DMP。无权限访问的位置整体跳过。
    /// </summary>
    public static DirectoryScanResult MeasureCrashDumps()
    {
        long size = 0, files = 0, skipped = 0;

        void AddDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(path, "*.dmp"))
            {
                try
                {
                    size += new FileInfo(file).Length;
                    files++;
                }
                catch
                {
                    skipped++;
                }
            }
        }

        try
        {
            AddDirectory(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CrashDumps"));
        }
        catch { }

        try
        {
            AddDirectory(@"C:\Windows\Minidump");
        }
        catch { }

        try
        {
            if (File.Exists(@"C:\Windows\MEMORY.DMP"))
            {
                size += new FileInfo(@"C:\Windows\MEMORY.DMP").Length;
                files++;
            }
        }
        catch
        {
            skipped++;
        }

        return new DirectoryScanResult(size, files, skipped);
    }

    /// <summary>字节数格式化为可读文本（B / KB / MB / GB）。</summary>
    public static string FormatSize(long bytes)
    {
        double value = bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:F2} {units[unit]}";
    }
}
