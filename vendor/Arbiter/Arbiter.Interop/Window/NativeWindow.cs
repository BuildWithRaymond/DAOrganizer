namespace Arbiter.Interop.Window;

public struct NativeWindow
{
    public IntPtr Handle { get; set; }
    public int ProcessId { get; set; }
    public required string ClassName { get; set; }
    public required string Title { get; set; }
}