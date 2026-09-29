using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using DAOrganizer.Core;
using DAOrganizer.Game;
namespace DAOrganizer.App;

public sealed partial class MainWindow
{
    private async Task Launch()
    {
        if(_app.Busy)throw new InvalidOperationException("Stop the current action before launching another client.");
        if(_selected!=null&&_app.Session(_selected)!=null){NativeInput.Focus(RequireSession().ProcessId);return;}
        _status.Text="Launching Dark Ages…";
        var session=await _app.Launch();
        _status.Text="Client launched. Sign in to begin tracking.";
        if(_selected!=null&&CredentialVault.Exists(_selected))
        {
            var name=_selected;
            await _app.RunOperation(t=>session.LoginSaved(name,t));
            _status.Text="Saved login submitted. Waiting for character.";
        }
    }
    private async Task EditCharacter(string? existing=null,Window? owner=null)
    {
        _app.RequireLiveProfile();
        var window=DialogWindow("Save character login",420);var panel=DialogPanel();
        var name=new TextBox{Watermark="Character name",MaxLength=12,Text=existing,IsReadOnly=existing!=null};var password=new TextBox{Watermark="Password (optional)",PasswordChar='●',MaxLength=32};
        panel.Children.Add(Text("Character",12,true));panel.Children.Add(name);panel.Children.Add(Text("Saved password",12,true));panel.Children.Add(password);
        panel.Children.Add(Text(existing==null?"Password is stored in Windows Credential Manager. Leave blank to log in manually.":"Leave password blank to keep the existing saved login.",12,true));
        var error=Text("",12);panel.Children.Add(error);
        panel.Children.Add(Button("Save character",()=>
        {
            try
            {
                if(_app.Busy)throw new InvalidOperationException("Stop the current action before editing accounts.");
                var n=name.Text?.Trim()??"";_app.Store.EnsureCharacter(n);
                if(!string.IsNullOrEmpty(password.Text)){CredentialVault.Save(n,password.Text);_app.Accounts.SetUpdate(n,true);}
                _app.Accounts.SetDisplay(n,true);password.Text="";_selected=n;_allAccounts=false;if(_location=="All items")_location="Inventory";window.Close();
            }
            catch(Exception ex){error.Text=ex.Message;}
            return Task.CompletedTask;
        },"primary"));window.Content=panel;await window.ShowDialog(owner??this);
    }
    private async Task Settings()
    {
        var window=DialogWindow("Settings",630);var panel=DialogPanel();
        var executable=new TextBox{Text=_app.ClientPath};var world=new TextBox{Text=_app.WorldLogsPath};
        panel.Children.Add(Text("Dark Ages executable",12,true));panel.Children.Add(executable);
        panel.Children.Add(Text("WorldLogs folder",12,true));panel.Children.Add(world);
        panel.Children.Add(Text("Travel uses walking, portals, and world maps and continues in the background. Stop cancels a trip; Escape also stops travel while the game is focused.",12,true));
        panel.Children.Add(Text("Database: "+_app.DataDirectory,12,true));
        panel.Children.Add(Button("Save settings",async()=>
        {
            _app.RequireLiveProfile();
            if(_app.Busy)throw new InvalidOperationException("Stop the current action before changing settings.");
            _app.ClientPath=executable.Text?.Trim()??"";_app.WorldLogsPath=world.Text?.Trim()??"";
            _app.SaveSettings();await _app.LoadWorld();_banks.ItemsSource=_app.Banks();window.Close();_status.Text=_app.WorldStatus;
        },"primary"));window.Content=panel;await window.ShowDialog(this);
    }
    private async Task Sort(bool category)
    {
        var session=RequireSession();var items=_app.Items(_selected!,"Inventory");
        var desired=SlotPlanner.Sort(items,_app.Pins(_selected!),category);
        var groups=category?string.Join(" · ",items.GroupBy(ItemCategories.Label).OrderBy(x=>ItemCategories.Order(x.First().Category)).ThenBy(x=>x.Key).Select(x=>$"{x.Key}: {x.Count()}")):null;
        await PreviewLayout(session,desired,category?"Sort by category":"Sort by name",groups);
    }
    private Task SaveLayout()
    {
        if(_selected==null)return Task.CompletedTask;
        var items=_app.Items(_selected,"Inventory");
        if(items.Length==0)throw new InvalidOperationException("Read inventory before saving a layout.");
        _app.Store.Put("layout/"+_selected.ToLowerInvariant(),items);_status.Text="Layout saved for "+_selected;return Task.CompletedTask;
    }
    private async Task ApplyLayout()
    {
        var session=RequireSession();var layout=_app.Store.Get<Item[]>("layout/"+_selected!.ToLowerInvariant())??throw new InvalidOperationException("Save a layout first.");
        var current=session.Inventory();var available=current.ToList();var pins=_app.Pins(_selected);
        var desired=current.Where(x=>pins.Contains(x.Slot)).ToDictionary(x=>x.Slot);available.RemoveAll(x=>pins.Contains(x.Slot));
        var missing=new List<string>();
        foreach(var target in layout.Where(x=>!pins.Contains(x.Slot)).OrderBy(x=>x.Slot))
        {
            var item=available.FirstOrDefault(x=>x.Name==target.Name&&x.Sprite==target.Sprite&&x.Color==target.Color);
            if(item==null){missing.Add(target.Name);continue;}
            desired[target.Slot]=item;available.Remove(item);
        }
        foreach(var item in available){var slot=SlotPlanner.BottomFirstSlots().First(x=>!desired.ContainsKey(x)&&!pins.Contains(x));desired[slot]=item;}
        PotionSlots.Place(desired);
        await PreviewLayout(session,desired,"Apply saved layout",missing.Count==0?null:"Missing items: "+string.Join(", ",missing));
    }
    private async Task PreviewLayout(GameSession session,Dictionary<int,Item> desired,string title,string? note=null)
    {
        var moves=SlotPlanner.Swaps(session.Inventory(),desired);
        if(moves.Count==0){_status.Text="Inventory already matches.";return;}
        var window=DialogWindow(title,430);var panel=DialogPanel();
        panel.Children.Add(Text($"{moves.Count} slot swaps",20));panel.Children.Add(Text("Potion slots take priority over pins: 1 Komadium, 2 Red Potion, 3 Exkuranum, 4 Dibenomum, 5 Hemloch, when carried. Other pins stay put. Each move waits for game confirmation.",12,true));
        if(note!=null)panel.Children.Add(Text(note,12,true));
        panel.Children.Add(new ScrollViewer{MaxHeight=240,Content=Text(string.Join("\n",moves.Select(x=>$"Slot {x.From:00} → {x.To:00}")),13)});
        var apply=new Button{Content="Apply changes"};apply.Classes.Add("primary");var accepted=false;apply.Click+=(_,_)=>{accepted=true;window.Close();};panel.Children.Add(apply);window.Content=panel;await window.ShowDialog(this);
        if(accepted)await _app.RunOperation(t=>session.ApplyLayout(desired,t));
    }
    private async Task ScanBank()
    {
        var session=RequireSession();
        if(_app.World==null)throw new InvalidOperationException("Load WorldLogs in Settings first.");
        if(_banks.SelectedItem is not BankDestination bank)throw new InvalidOperationException("Choose a bank destination first.");
        var routes=_app.World.Route(session.MapId,bank.MapId);
        var window=DialogWindow("Travel to bank",460);var panel=DialogPanel();
        panel.Children.Add(Text(bank.Name,20));panel.Children.Add(Text($"{routes.Count} map transitions. Character stays at bank when scan finishes.",13,true));
        panel.Children.Add(Text("Travel continues in the background. Stop cancels; Escape also stops travel while the game is focused. No items or gold will be transferred.",13,true));
        var accepted=false;var start=new Button{Content="Travel and scan"};start.Classes.Add("primary");start.Click+=(_,_)=>{accepted=true;window.Close();};panel.Children.Add(start);window.Content=panel;await window.ShowDialog(this);
        if(!accepted)return;
        void StopOnDamage()=>_app.Stop();session.Damaged+=StopOnDamage;
        try{await _app.RunOperation(t=>new Navigation(_app.World,Path.GetDirectoryName(_app.ClientPath)!).Travel(session,bank,t));_status.Text=session.Status;}
        finally{session.Damaged-=StopOnDamage;}
    }
    private async Task ItemDetail(Item item)
    {
        var window=DialogWindow(item.Name,440);var panel=DialogPanel();
        panel.Children.Add(Text(item.Name,22));panel.Children.Add(Text($"Slot {item.Slot} · Quantity {item.Quantity:N0}",14));
        if(item.MaxDurability is >0)panel.Children.Add(Text($"Durability {item.Durability:N0} / {item.MaxDurability:N0}",13,true));
        panel.Children.Add(Text("Drop / trade: unknown. Inventory data does not include this permission.",12,true));
        panel.Children.Add(RuleEditor(item));
        panel.Children.Add(Text("Category",12,true));var category=new TextBox{Text=item.Category};panel.Children.Add(category);
        panel.Children.Add(Text($"Group: {ItemCategories.Family(item)}. Automatic categories use Vorlof references, item names and observed equipment. Enter a category here to override it for this item name.",12,true));
        panel.Children.Add(Button("Save category",()=>{_app.Store.Put("category/"+item.Name.ToLowerInvariant(),string.IsNullOrWhiteSpace(category.Text)?"Other":category.Text.Trim());window.Close();return Task.CompletedTask;},"primary"));
        window.Content=panel;await window.ShowDialog(this);
    }
    private async Task Message(string title,string message)
    {
        var window=DialogWindow(title,470);var panel=DialogPanel();panel.Children.Add(Text(message,14));
        var close=new Button{Content="Close"};close.Click+=(_,_)=>window.Close();panel.Children.Add(close);window.Content=panel;await window.ShowDialog(this);
    }
    private Window DialogWindow(string title,double width)=>new(){Title=title,Width=width,SizeToContent=SizeToContent.Height,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#111214"),RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark};
    private static StackPanel DialogPanel()=>new(){Margin=new(24),Spacing=14};
    private void OnClosing(object? sender,WindowClosingEventArgs args)
    {
        if(_allowClose||_app.Sessions.Count==0)return;
        args.Cancel=true;WindowState=WindowState.Minimized;
        _status.Text="Minimized to keep game connections alive. Close game clients before exiting organizer.";
        // Keep an accessible taskbar window even on desktops without a system tray.
        if(_app.Sessions.All(x=>!x.Online))
        {
            _=ConfirmExit();
        }
    }
    private async Task ConfirmExit()
    {
        var window=DialogWindow("Exit organizer?",430);var panel=DialogPanel();
        panel.Children.Add(Text("Exiting disconnects clients launched through organizer. Game processes stay open.",14));
        var exit=new Button{Content="Exit organizer"};exit.Click+=(_,_)=>{_allowClose=true;window.Close();Close();};panel.Children.Add(exit);window.Content=panel;await window.ShowDialog(this);
    }
    public async Task RequestExit()
    {
        Show();WindowState=WindowState.Normal;Activate();
        if(_app.Sessions.Count==0){_allowClose=true;Close();return;}
        await ConfirmExit();
    }
}
