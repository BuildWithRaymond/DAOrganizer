using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DAOrganizer.App;

internal static class Tray
{
    public static TrayIcon Attach(App app,MainWindow window,Organizer organizer)
    {
        using var bitmap=new WriteableBitmap(new(32,32),new(96,96),PixelFormat.Rgba8888,AlphaFormat.Unpremul);
        using(var buffer=bitmap.Lock())
        {
            var pixels=new byte[buffer.RowBytes*32];
            for(var y=0;y<32;y++)for(var x=0;x<32;x++)
            {
                var cell=x>=5&&x<=26&&y>=5&&y<=26&&x!=15&&x!=16&&y!=15&&y!=16;
                var i=y*buffer.RowBytes+x*4;pixels[i]=(byte)(cell?182:30);pixels[i+1]=(byte)(cell?154:36);pixels[i+2]=(byte)(cell?103:43);pixels[i+3]=255;
            }
            Marshal.Copy(pixels,0,buffer.Address,pixels.Length);
        }
        var icon=new WindowIcon(bitmap);window.Icon=icon;
        var show=new NativeMenuItem("Open organizer");show.Click+=(_,_)=>{window.Show();window.WindowState=WindowState.Normal;window.Activate();};
        var stop=new NativeMenuItem("Stop current action");stop.Click+=(_,_)=>organizer.Stop();
        var exit=new NativeMenuItem("Exit organizer");exit.Click+=async(_,_)=>await window.RequestExit();
        var menu=new NativeMenu();menu.Items.Add(show);menu.Items.Add(stop);menu.Items.Add(new NativeMenuItemSeparator());menu.Items.Add(exit);
        var tray=new TrayIcon{Icon=icon,ToolTipText="DAOrganizer",Menu=menu,IsVisible=true};
        tray.Clicked+=(_,_)=>{window.Show();window.WindowState=WindowState.Normal;window.Activate();};
        TrayIcon.SetIcons(app,new TrayIcons{tray});return tray;
    }
}
