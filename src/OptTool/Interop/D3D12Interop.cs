using System.Runtime.InteropServices;

namespace OptTool.Interop;

/// <summary>
/// D3D12 设备创建与特性等级探测。
/// 不引入第三方 NuGet 包，纯 P/Invoke。
/// </summary>
internal static class D3D12Interop
{
    private static readonly Guid IID_ID3D12Device = new("189819F1-1DB6-4B57-BE54-1821339B85F7");

    // D3D_FEATURE_LEVEL 数值（从高到低）
    private static readonly (int Level, string Name)[] FeatureLevels =
    {
        (49664, "12_2"), // D3D_FEATURE_LEVEL_12_2
        (49408, "12_1"), // D3D_FEATURE_LEVEL_12_1
        (49152, "12_0"), // D3D_FEATURE_LEVEL_12_0
        (45312, "11_1"), // D3D_FEATURE_LEVEL_11_1
        (45056, "11_0"), // D3D_FEATURE_LEVEL_11_0
    };

    [DllImport("d3d12.dll", ExactSpelling = true)]
    private static extern int D3D12CreateDevice(nint pAdapter, int minFeatureLevel, in Guid riid, out nint ppDevice);

    /// <summary>
    /// 对指定适配器（IUnknown 指针）依次探测各 D3D_FEATURE_LEVEL，
    /// 返回最高支持的等级名称（如 "12_1"）。不支持 D3D12 时返回 null。
    /// 任何异常返回 null，不抛出。
    /// </summary>
    public static string? ProbeHighestFeatureLevel(nint adapterPtr)
    {
        if (adapterPtr == nint.Zero) return null;

        try
        {
            foreach (var (level, name) in FeatureLevels)
            {
                nint devicePtr;
                int hr = D3D12CreateDevice(adapterPtr, level, in IID_ID3D12Device, out devicePtr);
                if (hr >= 0 && devicePtr != nint.Zero)
                {
                    try { Marshal.Release(devicePtr); } catch { }
                    return name;
                }
            }
        }
        catch
        {
            // 探测失败返回 null
        }

        return null;
    }
}
