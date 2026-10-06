using Avalonia;
namespace DAOrganizer.App;
internal static class Program
{
    [STAThread]public static void Main(string[] args)
    {
        if(!args.Contains("--demo"))Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        using var instance=new Mutex(true,args.Contains("--demo")?"Local\\DAOrganizer.Demo":"Local\\DAOrganizer",out var ownsInstance);
        if(!ownsInstance)return;
        try{BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);}
        finally{instance.ReleaseMutex();}
    }
    public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
