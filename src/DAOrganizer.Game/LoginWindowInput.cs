using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace DAOrganizer.Game;

// Login input follows Excalibur-safe's window-addressed PostMessage path.
// Navigation continues to use foreground input separately.
public sealed class LoginWindowInput
{
    private readonly int _pid;
    private readonly DateTime _started;
    private readonly IntPtr _window;
    [DllImport("user32.dll",SetLastError=true)]private static extern bool PostMessage(IntPtr window,uint message,UIntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")]private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")]private static extern bool GetClientRect(IntPtr window,out Rect rectangle);
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}

    public LoginWindowInput(int pid)
    {
        using var process=Process.GetProcessById(pid);process.Refresh();
        _pid=pid;_started=process.StartTime;_window=process.MainWindowHandle;
        if(!process.ProcessName.Equals("Darkages",StringComparison.OrdinalIgnoreCase)||_window==IntPtr.Zero)
            throw new InvalidOperationException("Owned game window is unavailable.");
        Validate();
    }
    public void Validate()
    {
        GetWindowThreadProcessId(_window,out var owner);
        using var process=Process.GetProcessById(_pid);
        if(!IsWindow(_window)||owner!=_pid||process.HasExited||process.StartTime!=_started)
            throw new InvalidOperationException("Owned game window changed. Login stopped.");
    }
    private void Post(uint message,uint value,uint parameter)
    {
        Validate();
        if(!PostMessage(_window,message,(UIntPtr)value,(IntPtr)unchecked((int)parameter)))throw new Win32Exception();
    }
    public void Character(char ch)
    {
        if(ch is <' ' or >'~')throw new ArgumentException("Login requires printable ASCII text.");
        Post(0x102,ch,0);
    }
    public void PasswordDigit(char ch)
    {
        // Only the numeric placeholder is typed. The vault password stays in the proxy.
        if(ch is <'0' or >'9')throw new ArgumentException("Login placeholder must contain digits.");
        Key(ch);
    }
    public static uint KeyParameter(ushort key,bool release)
    {
        var input=NativeInput.CreateKeyInput(key,release);
        return 1u|((uint)input.Scan<<16)|((input.Flags&1)!=0?0x1000000u:0u)|(release?0xC0000000u:0u);
    }
    public void Key(ushort key)
    {
        Post(0x100,key,KeyParameter(key,false));Post(0x101,key,KeyParameter(key,true));
    }
    public void Click(int x,int y)
    {
        Validate();
        if(!GetClientRect(_window,out var rectangle)||rectangle.Right<=0||rectangle.Bottom<=0)
            throw new InvalidOperationException("Game window has no usable client area.");
        var point=(uint)((x*rectangle.Right/640)&0xffff)|((uint)(y*rectangle.Bottom/480)<<16);
        Post(0x200,0,point);Post(0x201,1,point);Post(0x202,0,point);
    }
}
