using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Deskup.Wpf.Helpers;

/// <summary>
/// 文件图标提取——使用 IShellItemImageFactory（Windows 资源管理器自身使用的接口），
/// 保证图标清晰度、色彩与 Alpha 通道与系统完全一致。
/// </summary>
public static class IconHelper
{
    private static readonly ImageSource DefaultIcon;

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    // IShellItemImageFactory::GetImage flags
    [Flags]
    private enum SIIGBF : uint
    {
        RESIZETOFIT      = 0x00,
        BIGGERSIZEOK     = 0x01,
        MEMORYONLY       = 0x02,
        ICONONLY         = 0x04,
        THUMBNAILONLY    = 0x08,
        INCACHEONLY      = 0x10,
        CROPTOSQUARE     = 0x20,
        WIDETHUMBNAILS   = 0x40,
        ICONBACKGROUND   = 0x80,
        SCALEUP          = 0x100,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int cx; public int cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(
            [In] NativeSize size,
            [In] SIIGBF flags,
            out IntPtr phbm);
    }

    static IconHelper()
    {
        var visual = new System.Windows.Controls.Border
        {
            Width = 16, Height = 16,
            Background = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))
        };
        var render = new RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Pbgra32);
        visual.Measure(new System.Windows.Size(16, 16));
        visual.Arrange(new Rect(0, 0, 16, 16));
        render.Render(visual);
        render.Freeze();
        DefaultIcon = render;
    }

    /// <summary>
    /// 提取文件图标——与 Windows 资源管理器完全一致的渲染质量。
    /// 使用 IShellItemImageFactory（Explorer 自身的图标接口），
    /// 在目标尺寸下直接获取图标 HBITMAP，无需二次缩放，Alpha 通道正确。
    /// </summary>
    public static ImageSource GetFileIcon(string path, int size = 48)
    {
        try
        {
            var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
            if (hr < 0 || factory == null) return DefaultIcon;

            var ns = new NativeSize { cx = size, cy = size };
            hr = factory.GetImage(
                ns,
                SIIGBF.ICONONLY | SIIGBF.BIGGERSIZEOK | SIIGBF.SCALEUP,
                out var hBitmap);

            if (hr < 0 || hBitmap == IntPtr.Zero) return DefaultIcon;

            var bs = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bs.Freeze();
            DeleteObject(hBitmap);
            return bs;
        }
        catch { return DefaultIcon; }
    }
}
