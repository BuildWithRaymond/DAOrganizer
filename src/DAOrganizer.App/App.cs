using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
namespace DAOrganizer.App;
public sealed class App:Application
{
    public override void Initialize()
    {
        RequestedThemeVariant=ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://DAOrganizer/")){Source=new Uri("avares://DAOrganizer/Styles.axaml")});
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var organizer=new Organizer(demo:desktop.Args?.Contains("--demo")==true);var window=new MainWindow(organizer);desktop.MainWindow=window;
            var tray=Tray.Attach(this,window,organizer);
            desktop.Exit+=(_,_)=>
            {
                var busy=organizer.Busy;var connected=organizer.HasOpenClients;
                organizer.Updates.PrepareExit(busy,connected);
                tray.Dispose();organizer.Dispose();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
