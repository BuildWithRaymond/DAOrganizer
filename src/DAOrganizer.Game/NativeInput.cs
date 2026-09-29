using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace DAOrganizer.Game;

public static class NativeInput
{
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion {[FieldOffset(0)]public Keyboard Keyboard;[FieldOffset(0)]public Mouse Mouse;}
    [StructLayout(LayoutKind.Sequential)] internal struct Keyboard{public ushort Key,Scan;public uint Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)] private struct Mouse{public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll",SetLastError=true)]private static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")]private static extern uint MapVirtualKey(uint key,uint mode);
    [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern bool GetClientRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")]private static extern bool ClientToScreen(IntPtr window,ref Point point);
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]public static extern short GetAsyncKeyState(int key);
    public static void Focus(int pid)
    {
        using var process=Process.GetProcessById(pid);process.Refresh();
        if(process.MainWindowHandle==IntPtr.Zero)throw new InvalidOperationException("Game window is not ready yet.");
        SetForegroundWindow(process.MainWindowHandle);
    }
    public static bool HasFocus(int pid){GetWindowThreadProcessId(GetForegroundWindow(),out var current);return current==pid;}
    public static void Click(int pid,int x,int y)
    {
        if(!HasFocus(pid))throw new InvalidOperationException("Game lost focus. Login stopped.");
        var window=GetForegroundWindow();GetClientRect(window,out var rect);
        var point=new Point{X=x*rect.Right/640,Y=y*rect.Bottom/480};ClientToScreen(window,ref point);SetCursorPos(point.X,point.Y);
        Input[] inputs=[new(){Type=0,Data=new(){Mouse=new(){Flags=2}}},new(){Type=0,Data=new(){Mouse=new(){Flags=4}}}];
        if(SendInput(2,inputs,Marshal.SizeOf<Input>())!=2)throw new Win32Exception();
    }
    public static void Key(int pid,ushort key)
    {
        if(!HasFocus(pid))throw new InvalidOperationException("Game lost focus. Operation stopped.");
        Input[] inputs=[new(){Type=1,Data=new(){Keyboard=CreateKeyInput(key,false)}},new(){Type=1,Data=new(){Keyboard=CreateKeyInput(key,true)}}];
        if(SendInput(2,inputs,Marshal.SizeOf<Input>())!=2)throw new Win32Exception();
    }
    internal static Keyboard CreateKeyInput(ushort key,bool release)
    {
        // Dark Ages reads the WM_KEYDOWN scan code, not its virtual-key value.
        var scan=MapVirtualKey(key,4); // MAPVK_VK_TO_VSC_EX includes the E0 prefix.
        if(scan==0)throw new ArgumentException("Key has no hardware scan code.",nameof(key));
        var extended=(scan&0xff00)==0xe000||key is >=0x21 and <=0x28 or 0x2D or 0x2E or 0x6F or 0xA3 or 0xA5;
        return new(){Scan=(ushort)(scan&0xff),Flags=8u|(extended?1u:0u)|(release?2u:0u)};
    }
    public static void Text(int pid,string text)
    {
        foreach(var ch in text)
        {
            if(!HasFocus(pid))throw new InvalidOperationException("Game lost focus. Login stopped.");
            Input[] inputs=[new(){Type=1,Data=new(){Keyboard=new(){Scan=ch,Flags=4}}},new(){Type=1,Data=new(){Keyboard=new(){Scan=ch,Flags=6}}}];
            if(SendInput(2,inputs,Marshal.SizeOf<Input>())!=2)throw new Win32Exception();
        }
    }
}
