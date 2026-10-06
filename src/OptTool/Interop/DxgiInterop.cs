using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace OptTool.Interop;

/// <summary>
/// DXGI 适配器描述（IDXGIAdapter::GetDesc 使用，vtable 占位需要）。
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DXGI_ADAPTER_DESC
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Description;
    public uint VendorId;
    public uint DeviceId;
    public uint SubSysId;
    public uint Revision;
    public nuint DedicatedVideoMemory;
    public nuint DedicatedSystemMemory;
    public nuint SharedSystemMemory;
    public long AdapterLuid;
}

/// <summary>
/// DXGI 适配器描述 1（IDXGIAdapter1::GetDesc1 使用，含 Flags 可判断软件适配器）。
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DXGI_ADAPTER_DESC1
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Description;
    public uint VendorId;
    public uint DeviceId;
    public uint SubSysId;
    public uint Revision;
    public nuint DedicatedVideoMemory;
    public nuint DedicatedSystemMemory;
    public nuint SharedSystemMemory;
    public long AdapterLuid;
    public uint Flags; // DXGI_ADAPTER_FLAG：bit1 = SOFTWARE（值 2），bit0 为 REMOTE
}

/// <summary>DXGI IID 与常量。</summary>
internal static class DxgiGuids
{
    public const uint DXGI_ADAPTER_FLAG_SOFTWARE = 1;
    public static readonly Guid IID_IDXGIFactory1 = new("770AAE78-F26F-4DBA-A829-253C83D1B387");
    public static readonly Guid IID_IDXGIAdapter1 = new("29038F61-3839-4626-91FD-086879011A05");
}

// ---- COM 接口声明（GeneratedComInterface，源生成 COM，Native AOT 安全） ----

[GeneratedComInterface]
[Guid("AEC22FB8-76F3-4639-9BE0-28EB43A67A2E")]
internal partial interface IDXGIObject
{
    int SetPrivateData(in Guid Name, uint DataSize, nint pData);
    int SetPrivateDataInterface(in Guid Name, nint pObject);
    int GetPrivateData(in Guid Name, ref uint pDataSize, nint pData);
    int GetParent(in Guid riid, out nint pParent);
}

[GeneratedComInterface]
[Guid("2411E7E1-12AC-4CCF-BD14-9798E8534D0C")]
internal partial interface IDXGIAdapter : IDXGIObject
{
    int EnumOutputs(uint Output, out nint ppOutput);
    int GetDesc(nint pDesc);
    int CheckInterfaceSupport(in Guid InterfaceName, out long pUMDVersion);
}

[GeneratedComInterface]
[Guid("29038F61-3839-4626-91FD-086879011A05")]
internal partial interface IDXGIAdapter1 : IDXGIAdapter
{
    int GetDesc1(nint pDesc);
}

[GeneratedComInterface]
[Guid("7B7166EC-21C7-44AE-B21A-C9AE321AE396")]
internal partial interface IDXGIFactory : IDXGIObject
{
    int EnumAdapters(uint Adapter, out nint ppAdapter);
    int MakeWindowAssociation(nint WindowHandle, uint Flags);
    int GetWindowAssociation(out nint pWindowHandle);
    int CreateSwapChain(nint pDevice, nint pDesc, out nint ppSwapChain);
    int CreateSoftwareAdapter(nint Module, out nint ppAdapter);
}

[GeneratedComInterface]
[Guid("770AAE78-F26F-4DBA-A829-253C83D1B387")]
internal partial interface IDXGIFactory1 : IDXGIFactory
{
    int EnumAdapters1(uint Adapter, out nint ppAdapter);
    int IsCurrent();
}

/// <summary>
/// DXGI 工厂与适配器枚举的封装。
/// 上方 IDXGIFactory1 / IDXGIAdapter1 等接口用 [GeneratedComInterface] 声明，
/// 作为 COM vtable 契约定义（Native AOT 安全，不依赖旧式 COM 导入特性）。
/// 实际调用通过原始 vtable 函数指针（纯 P/Invoke），避免运行时 COM 互操作
/// （Marshal.GetObjectForIUnknown / ReleaseComObject 与源生成 COM 不兼容，SYSLIB1099）。
/// 所有方法包 try/catch，失败返回空列表，不抛异常。
/// </summary>
internal static class DxgiEnumerator
{
    private const int S_OK = 0;
    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);

    // vtable 槽位计算（从 0 开始）：
    //   IUnknown: 3 (QueryInterface/AddRef/Release) → 槽 0-2
    //   IDXGIObject: 4 → 槽 3-6
    //   IDXGIFactory: 5 → 槽 7-11
    //   IDXGIFactory1.EnumAdapters1 → 槽 12
    private const int VTABLE_SLOT_EnumAdapters1 = 12;

    //   IDXGIAdapter: 3 (EnumOutputs/GetDesc/CheckInterfaceSupport) → 槽 7-9
    //   IDXGIAdapter1.GetDesc1 → 槽 10
    private const int VTABLE_SLOT_GetDesc1 = 10;

    //   IUnknown.Release → 槽 2
    private const int VTABLE_SLOT_Release = 2;

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory2(uint Flags, in Guid riid, out nint ppFactory);

    // COM 方法委托（StdCall 调用约定）
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Fn(nint factoryPtr, uint adapterIndex, out nint adapterPtr);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Fn(nint adapterPtr, nint descPtr);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseFn(nint ptr);

    /// <summary>从 COM 对象指针读取 vtable 中指定槽位的函数指针。</summary>
    private static nint GetVtableMethod(nint comPtr, int slot)
    {
        nint vtbl = Marshal.ReadIntPtr(comPtr);
        return Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size);
    }

    /// <summary>调用 IUnknown::Release 释放原生引用。</summary>
    private static void ReleaseComPtr(nint ptr)
    {
        if (ptr == nint.Zero) return;
        try
        {
            nint fnPtr = GetVtableMethod(ptr, VTABLE_SLOT_Release);
            var release = Marshal.GetDelegateForFunctionPointer<ReleaseFn>(fnPtr);
            release(ptr);
        }
        catch { /* 忽略释放错误 */ }
    }

    /// <summary>
    /// 调用 IDXGIAdapter1::GetDesc1（原始 vtable + 手动 struct 封送）。
    /// 成功返回描述，失败返回 null。
    /// </summary>
    private static DXGI_ADAPTER_DESC1? TryGetDesc1(nint adapterPtr)
    {
        if (adapterPtr == nint.Zero) return null;

        int size = Marshal.SizeOf<DXGI_ADAPTER_DESC1>();
        nint descPtr = Marshal.AllocHGlobal(size);
        try
        {
            byte[] zeros = new byte[size];
            Marshal.Copy(zeros, 0, descPtr, size);

            nint fnPtr = GetVtableMethod(adapterPtr, VTABLE_SLOT_GetDesc1);
            var getDesc1 = Marshal.GetDelegateForFunctionPointer<GetDesc1Fn>(fnPtr);
            if (getDesc1(adapterPtr, descPtr) == S_OK)
            {
                return Marshal.PtrToStructure<DXGI_ADAPTER_DESC1>(descPtr);
            }
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(descPtr);
        }
    }

    /// <summary>
    /// 枚举全部 DXGI 适配器（含软件适配器），返回描述结构列表。
    /// 任何失败返回空列表。
    /// </summary>
    public static List<DXGI_ADAPTER_DESC1> EnumerateAdapters()
    {
        var results = new List<DXGI_ADAPTER_DESC1>();
        nint factoryPtr = nint.Zero;

        try
        {
            int hr = CreateDXGIFactory2(0, in DxgiGuids.IID_IDXGIFactory1, out factoryPtr);
            if (hr < 0 || factoryPtr == nint.Zero) return results;

            nint enumFnPtr = GetVtableMethod(factoryPtr, VTABLE_SLOT_EnumAdapters1);
            var enumAdapters1 = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Fn>(enumFnPtr);

            for (uint i = 0; ; i++)
            {
                nint adapterPtr = nint.Zero;
                try
                {
                    int enumHr = enumAdapters1(factoryPtr, i, out adapterPtr);
                    if (enumHr == DXGI_ERROR_NOT_FOUND || adapterPtr == nint.Zero) break;

                    var desc = TryGetDesc1(adapterPtr);
                    if (desc.HasValue)
                    {
                        results.Add(desc.Value);
                    }
                }
                catch
                {
                    // 单个适配器失败跳过
                }
                finally
                {
                    ReleaseComPtr(adapterPtr);
                }
            }
        }
        catch
        {
            // 整体失败返回已收集部分（可能为空）
        }
        finally
        {
            ReleaseComPtr(factoryPtr);
        }

        return results;
    }

    /// <summary>
    /// 按描述名查找适配器，返回其 IUnknown 原生指针（调用方负责 Release）。
    /// 未找到返回 nint.Zero。用于 D3D12CreateDevice 探测。
    /// </summary>
    public static nint GetAdapterIUnknownByName(string adapterDescription)
    {
        nint factoryPtr = nint.Zero;

        try
        {
            int hr = CreateDXGIFactory2(0, in DxgiGuids.IID_IDXGIFactory1, out factoryPtr);
            if (hr < 0 || factoryPtr == nint.Zero) return nint.Zero;

            nint enumFnPtr = GetVtableMethod(factoryPtr, VTABLE_SLOT_EnumAdapters1);
            var enumAdapters1 = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Fn>(enumFnPtr);

            for (uint i = 0; ; i++)
            {
                nint adapterPtr = nint.Zero;
                try
                {
                    int enumHr = enumAdapters1(factoryPtr, i, out adapterPtr);
                    if (enumHr == DXGI_ERROR_NOT_FOUND || adapterPtr == nint.Zero) break;

                    var desc = TryGetDesc1(adapterPtr);
                    if (desc.HasValue &&
                        string.Equals(desc.Value.Description, adapterDescription, StringComparison.OrdinalIgnoreCase))
                    {
                        // 匹配：AddRef 一份给调用方（当前引用在 finally 中 Release）
                        try { Marshal.AddRef(adapterPtr); } catch { }
                        return adapterPtr;
                    }
                }
                catch
                {
                    // 跳过
                }
                finally
                {
                    ReleaseComPtr(adapterPtr);
                }
            }
        }
        catch
        {
            // 失败返回 Zero
        }
        finally
        {
            ReleaseComPtr(factoryPtr);
        }

        return nint.Zero;
    }
}
