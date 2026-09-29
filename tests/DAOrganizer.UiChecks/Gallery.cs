using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DAOrganizer.App;
using DAOrganizer.Core;

internal static class Gallery
{
    public static void Run(string output)
    {
        var profile=Path.Combine(output,"demo-must-stay-in-memory");
        using var app=new Organizer(profile,demo:true);
        if(Directory.Exists(profile)||!app.IsDemo||app.Store.Characters().Count!=6)throw new Exception("Demo must use an isolated in-memory profile.");
        void Blocked(Func<Task> action)
        {
            try{action().GetAwaiter().GetResult();throw new Exception("Demo allowed a live action.");}
            catch(InvalidOperationException e) when(e.Message.StartsWith("Demo mode")){}
        }
        Blocked(async()=>{await app.Launch();});Blocked(()=>app.UpdateAccounts());Blocked(()=>app.RunMaintenance([]));
        Blocked(()=>{app.SaveSettings();return Task.CompletedTask;});
        var ran=false;Blocked(()=>app.RunOperation(_=>{ran=true;return Task.CompletedTask;}));
        if(ran||app.Sessions.Count!=0)throw new Exception("Demo created a live operation.");
        var window=new MainWindow(app){Width=1440,Height=1040};window.Show();
        void Settle(){for(var i=0;i<10;i++){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Thread.Sleep(40);}}
        void Capture(string name,Window? target=null)
        {
            Settle();using var image=(target??window).CaptureRenderedFrame()??throw new Exception("No frame");
            image.Save(Path.Combine(output,name+".png"));Console.WriteLine("Gallery: "+name);
        }
        void Click(string text)
        {
            window.GetVisualDescendants().OfType<Button>().First(x=>x.Content as string==text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Settle();
        }
        Capture("collection");
        var potion=window.GetVisualDescendants().OfType<Button>().Single(x=>x.Tag is ItemGroup g&&g.Item.Name=="Komadium");
        potion.BringIntoView();Settle();potion.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Capture("item-details");
        Click("Close details");
        window.GetVisualDescendants().OfType<Button>().Single(x=>x.Tag as string=="Aisling").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Capture("inventory");
        Click("Account manager");var manager=window.OwnedWindows.Single(x=>x.Title=="Account manager");Capture("accounts",manager);manager.Close();
        window.Width=980;window.Height=720;Capture("inventory-compact");
        Click("All accounts");window.GetVisualDescendants().OfType<TextBox>().First().Text="No such item";Capture("empty-state");
        window.Close();Console.WriteLine("Demo isolation and gallery checks passed.");
    }
}
