using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using SkiaSharp;

namespace HomeBase.Windows;

public static class Windows
{
    internal static unsafe Tuple<string,string>? OpenFilePicker(HWND owner)
    {
        // COM must be initialized on the thread that shows the dialog.
        HRESULT hr = PInvoke.CoInitializeEx(
            null,
            COINIT.COINIT_APARTMENTTHREADED | COINIT.COINIT_DISABLE_OLE1DDE);

        bool shouldUninitialize = hr.Succeeded;

        try
        {
            IFileOpenDialog dialog = FileOpenDialog.CreateInstance<IFileOpenDialog>();

            // Optional: set title
            dialog.SetTitle("Choose a file");

            // Optional: only allow existing files
            FILEOPENDIALOGOPTIONS options;
            dialog.GetOptions(&options);
            dialog.SetOptions(options | FILEOPENDIALOGOPTIONS.FOS_FILEMUSTEXIST);

            try
            {
                dialog.Show(owner);
            }
            catch (Exception ex)
            {
                // cancelled by user
                return null;
            }

            // // User cancelled
            // if (hr == HRESULT_FROM_WIN32(WIN32_ERROR.ERROR_CANCELLED))
            // {
            //     return null;
            // }
            //
            // hr.ThrowOnFailure();

            dialog.GetResult(out IShellItem result);

            PWSTR pathPtr;
            PWSTR fileNmPtr;
            result.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, &pathPtr);
            result.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, &fileNmPtr);
            try
            {
                return new Tuple<string, string>(fileNmPtr.ToString(), pathPtr.ToString());
            }
            finally
            {
                PInvoke.CoTaskMemFree(pathPtr);
                PInvoke.CoTaskMemFree(fileNmPtr);
            }
        }
        finally
        {
            if (shouldUninitialize)
            {
                PInvoke.CoUninitialize();
            }
        }

        // static HRESULT HRESULT_FROM_WIN32(WIN32_ERROR error)
        // {
        //     return (HRESULT)(int)(0x80070000u | (uint)error);
        // }
    }

    internal unsafe static SKImage? LoadIconImageForFile(string path)
    {
        // load image from filepath
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        const int requestedIconSize = 128;

        HRESULT hr = PInvoke.CoInitializeEx(
            null,
            COINIT.COINIT_APARTMENTTHREADED | COINIT.COINIT_DISABLE_OLE1DDE);

        bool shouldUninitialize = hr.Succeeded;

        HBITMAP bitmapHandle = HBITMAP.Null;

        try
        {
            Guid shellItemImageFactoryId = typeof(IShellItemImageFactory).GUID;

            hr = PInvoke.SHCreateItemFromParsingName(
                path,
                null,
                in shellItemImageFactoryId,
                out object imageFactoryObject);

            if (hr.Failed || imageFactoryObject is not IShellItemImageFactory imageFactory)
            {
                return null;
            }

            SIZE size = new()
            {
                cx = requestedIconSize,
                cy = requestedIconSize
            };

            try
            {
                imageFactory.GetImage(
                    size,
                    SIIGBF.SIIGBF_ICONONLY | SIIGBF.SIIGBF_BIGGERSIZEOK,
                    &bitmapHandle);
            }
            catch
            {
                return null;
            }

            if (bitmapHandle == HBITMAP.Null)
            {
                return null;
            }

            return CreateSkImageFromHBitmap(bitmapHandle);
        }
        finally
        {
            if (bitmapHandle != HBITMAP.Null)
            {
                _ = PInvoke.DeleteObject(bitmapHandle);
            }

            if (shouldUninitialize)
            {
                PInvoke.CoUninitialize();
            }
        }
    }

    internal unsafe static SKImage? CreateSkImageFromHBitmap(HBITMAP bitmapHandle)
    {
        BITMAP bitmap = default;

        int objectSize = PInvoke.GetObject(
            new HGDIOBJ(bitmapHandle.Value),
            sizeof(BITMAP),
            &bitmap);

        if (objectSize == 0 || bitmap.bmWidth <= 0 || bitmap.bmHeight <= 0)
        {
            return null;
        }

        int width = bitmap.bmWidth;
        int height = bitmap.bmHeight;

        BITMAPINFO bitmapInfo = default;
        bitmapInfo.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        bitmapInfo.bmiHeader.biWidth = width;
        bitmapInfo.bmiHeader.biHeight = -height;
        bitmapInfo.bmiHeader.biPlanes = 1;
        bitmapInfo.bmiHeader.biBitCount = 32;
        bitmapInfo.bmiHeader.biCompression = 0;

        int stride = width * 4;
        int byteCount = stride * height;
        byte[] pixels = new byte[byteCount];

        HDC screenDc = PInvoke.GetDC(HWND.Null);

        try
        {
            fixed (byte* pixelsPtr = pixels)
            {
                int scanLines = PInvoke.GetDIBits(
                    screenDc,
                    bitmapHandle,
                    0,
                    (uint)height,
                    pixelsPtr,
                    &bitmapInfo,
                    DIB_USAGE.DIB_RGB_COLORS);

                if (scanLines == 0)
                {
                    return null;
                }
            }
        }
        finally
        {
            _ = PInvoke.ReleaseDC(HWND.Null, screenDc);
        }

        SKImageInfo imageInfo = new(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);

        using SKBitmap skBitmap = new(imageInfo);

        nint skiaPixels = skBitmap.GetPixels();

        if (skiaPixels == 0)
        {
            return null;
        }

        Marshal.Copy(pixels, 0, skiaPixels, byteCount);

        return SKImage.FromBitmap(skBitmap);
    }
}