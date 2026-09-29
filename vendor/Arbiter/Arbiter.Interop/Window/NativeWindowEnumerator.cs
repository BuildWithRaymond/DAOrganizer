using System.Text;
using Arbiter.Interop.Win32;

namespace Arbiter.Interop.Window;

public static class NativeWindowEnumerator
{
    public static IEnumerable<NativeWindow> FindWindows(string? className, string? title = null)
    {
        var windows = new List<NativeWindow>();
        NativeMethods.EnumWindows((windowHandle, lParam) =>
        {
            var threadId = NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);

            var windowClassNameBuffer = new StringBuilder(256);
            var windowClassNameLength =
                NativeMethods.GetClassName(windowHandle, windowClassNameBuffer, windowClassNameBuffer.Capacity);
            var windowClassName = windowClassNameBuffer.ToString(0, windowClassNameLength);

            // Check for matching class name
            if (className is not null && !string.Equals(className, windowClassName, StringComparison.Ordinal))
            {
                return true;
            }

            var windowTitleLength = NativeMethods.GetWindowTextLength(windowHandle);
            var windowTitleBuffer = new StringBuilder(windowTitleLength + 1);
            windowTitleLength =
                NativeMethods.GetWindowText(windowHandle, windowTitleBuffer, windowTitleBuffer.Capacity);
            var windowTitle = windowTitleBuffer.ToString(0, windowTitleLength);

            if (title is not null && !string.Equals(title, windowTitle, StringComparison.Ordinal))
            {
                return true;
            }

            var window = new NativeWindow
            {
                Handle = windowHandle,
                ProcessId = processId,
                ClassName = windowClassName,
                Title = windowTitle,
            };

            windows.Add(window);
            return true;
        }, IntPtr.Zero);

        return windows;
    }
}