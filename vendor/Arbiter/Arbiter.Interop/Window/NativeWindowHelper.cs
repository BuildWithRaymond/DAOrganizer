using System.ComponentModel;
using Arbiter.Interop.Win32;

namespace Arbiter.Interop.Window;

public static class NativeWindowHelper
{
    public static void SetWindowTitle(IntPtr windowHandle, string newTitle)
    {
        if (!NativeMethods.SetWindowText(windowHandle, newTitle))
        {
            throw new Win32Exception();
        }
    }

    public static void BringToFront(IntPtr windowHandle)
    {
        if (!NativeMethods.ShowWindow(windowHandle, Win32ShowWindowCommand.Restore))
        {
            throw new Win32Exception();
        }

        if (!NativeMethods.SetForegroundWindow(windowHandle))
        {
            throw new Win32Exception();
        }
    }
}