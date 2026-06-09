using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace HomeBase.Windows;

public static class Windows
{
    static unsafe Tuple<string,string>? OpenFilePicker(HWND owner)
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

}