using System.Runtime.InteropServices;

namespace Arbiter.Interop.Win32;

[StructLayout(LayoutKind.Sequential)]
internal struct Win32SecurityAttributes
{
    public int Size { get; set; }
    public IntPtr SecurityDescriptor { get; set; }
    public bool InheritHandle { get; set; }
}