using System;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;

namespace AccountaBall.Platform.Capture;

/// Native glue for <c>Windows.Graphics.Capture</c>: create an <see
/// cref="IDirect3DDevice"/> for the frame pool, and reach the WinRT-projected
/// COM interop interfaces (<c>IGraphicsCaptureItemInterop</c>,
/// <c>IDirect3DDxgiInterfaceAccess</c>) that have no public C# projection. This is
/// the standard Win32CaptureSample pattern.
internal static class Direct3D11Interop
{
    // --- d3d11.dll ---

    [DllImport("d3d11.dll", EntryPoint = "D3D11CreateDevice", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = true)]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter, uint driverType, IntPtr software, uint flags,
        IntPtr pFeatureLevels, uint featureLevels, uint sdkVersion,
        out IntPtr ppDevice, out uint pFeatureLevel, out IntPtr ppImmediateContext);

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true, ExactSpelling = true, PreserveSig = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    private const uint D3D_DRIVER_TYPE_HARDWARE = 1;
    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    private const uint D3D11_SDK_VERSION = 7;

    /// IID for IDXGIDevice (QI from the D3D11 device to hand to the WinRT factory).
    private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    /// Create a WinRT <see cref="IDirect3DDevice"/> over a fresh hardware D3D11
    /// device (BGRA support, required by the OCR-friendly frame pool format).
    public static IDirect3DDevice CreateDevice()
    {
        int hr = D3D11CreateDevice(
            IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0, D3D11_SDK_VERSION,
            out IntPtr d3dDevice, out _, out IntPtr context);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);

        try
        {
            // QI the D3D11 device for IDXGIDevice, hand it to the WinRT factory.
            Guid dxgiIid = IID_IDXGIDevice;
            hr = Marshal.QueryInterface(d3dDevice, ref dxgiIid, out IntPtr pDxgi);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
            try
            {
                hr = CreateDirect3D11DeviceFromDXGIDevice(pDxgi, out IntPtr pWinrtDevice);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                try
                {
                    // The returned IInspectable projects to IDirect3DDevice.
                    return (IDirect3DDevice)Marshal.GetObjectForIUnknown(pWinrtDevice);
                }
                finally { Marshal.Release(pWinrtDevice); }
            }
            finally { Marshal.Release(pDxgi); }
        }
        finally
        {
            if (context != IntPtr.Zero) Marshal.Release(context);
            if (d3dDevice != IntPtr.Zero) Marshal.Release(d3dDevice);
        }
    }
}

/// <c>IGraphicsCaptureItemInterop</c> — the COM factory that turns an HWND/HMONITOR
/// into a <c>GraphicsCaptureItem</c>. No public WinRT projection exists.
[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
    IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
}
