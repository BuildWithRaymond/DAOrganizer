using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DAOrganizer.Core;
using DAOrganizer.Game;
namespace DAOrganizer.App;

public sealed partial class MainWindow:Window
{
    private readonly Organizer _app;
    private readonly ItemImages _images=new();
    public Task ArtworkReady {get;private set;}=Task.CompletedTask;
    public bool HasItemSprites=>_images.Loaded;
    private readonly StackPanel _characters=new(){Spacing=4};
    private readonly TextBox _search=new(){Watermark="Search your collection…",Width=360};
    private readonly TextBlock _title=Text("Your characters",32),_detail=Text("Launch a client to begin.",12,true),_status=Text("Ready",11,true);
    private readonly ContentControl _body=new();
    private readonly WrapPanel _toolbar=new(){Orientation=Orientation.Horizontal,ItemSpacing=8,LineSpacing=6};
    private readonly StackPanel _tabs=new(){Orientation=Orientation.Horizontal,Spacing=3};
    private readonly ComboBox _banks=new(){MinWidth=230,MaxWidth=330,PlaceholderText="Choose bank destination"};
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private string? _selected;
    private string _location="Inventory",_renderKey="",_characterKey="";
    private bool _allowClose;
    private bool _allAccounts;
    private Button? _launchButton;
    public MainWindow(Organizer app)
    {
        _app=app;Title="DA Organizer — The Celtic Collection";Width=1280;Height=880;MinWidth=980;MinHeight=720;
        if(app.IsDemo){_allAccounts=true;_location="All items";}
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new Grid{ColumnDefinitions=new("220,*"),RowDefinitions=new("*,Auto")};
        var sidebar=new DockPanel{LastChildFill=true,Margin=new(14,26,14,18)};
        var brand=new StackPanel{Spacing=12,Margin=new(4,0,4,24)};
        var identity=new Grid{ColumnDefinitions=new("60,*")};identity.Children.Add(new CelticSeal{Width=52,Height=52});
        var wordmark=new StackPanel{Spacing=2,VerticalAlignment=VerticalAlignment.Center};
        wordmark.Children.Add(new TextBlock{Text="DA Organizer",FontFamily=new("Georgia"),FontSize=18,Foreground=Brush("#F2E8D2")});
        wordmark.Children.Add(new TextBlock{Text="THE COLLECTION",FontSize=8,LetterSpacing=1.4,Foreground=Brush("#D6B46A")});Grid.SetColumn(wordmark,1);identity.Children.Add(wordmark);
        brand.Children.Add(identity);brand.Children.Add(new CelticRule{Height=12,Opacity=.7});
        brand.Children.Add(new TextBlock{Text="CHARACTERS",FontSize=9,LetterSpacing=2,Foreground=Brush("#86837B"),Margin=new(8,5,0,0)});DockPanel.SetDock(brand,Dock.Top);sidebar.Children.Add(brand);
        var sideBottom=new StackPanel{Spacing=8};
        sideBottom.Children.Add(Button("+  Add character",()=>EditCharacter(),"quiet"));
        sideBottom.Children.Add(Button("Account manager",AccountManager,"quiet"));
        sideBottom.Children.Add(Button("Settings",()=>Settings(),"quiet"));
        DockPanel.SetDock(sideBottom,Dock.Bottom);sidebar.Children.Add(sideBottom);
        sidebar.Children.Add(new ScrollViewer{Content=_characters});
        root.Children.Add(new Border{Background=Brush("#101113"),BorderBrush=Brush("#393124"),BorderThickness=new(0,0,1,0),Child=sidebar});
        var main=new Grid{RowDefinitions=new("Auto,Auto,Auto,Auto,*"),Margin=new(26,28,26,18)};Grid.SetColumn(main,1);root.Children.Add(main);
        var header=new Grid{ColumnDefinitions=new("*,Auto")};var heading=new StackPanel{Spacing=8};
        heading.Children.Add(new TextBlock{Text="DARK AGES  /  INVENTORY COMPANION",FontSize=9,LetterSpacing=1.8,Foreground=Brush("#C1A064")});
        heading.Children.Add(_title);heading.Children.Add(_detail);header.Children.Add(heading);
        var launch=Button("Launch client",Launch,"primary");_launchButton=launch;Grid.SetColumn(launch,1);header.Children.Add(launch);main.Children.Add(header);
        var searchRow=new Grid{ColumnDefinitions=new("*,Auto"),Margin=new(0,22,0,16)};Grid.SetRow(searchRow,1);main.Children.Add(searchRow);
        _search.HorizontalAlignment=HorizontalAlignment.Left;_search.TextChanged+=(_,_)=>Refresh(true);searchRow.Children.Add(_search);
        var stop=Button("Stop",()=>{_app.Stop();_status.Text="Stopped";return Task.CompletedTask;},"danger");Grid.SetColumn(stop,1);searchRow.Children.Add(stop);
        foreach(var location in new[]{"All items","Inventory","Equipment","Bank"})
        {
            var tab=Button(location,()=>{_location=location;_search.Text="";Refresh(true);return Task.CompletedTask;},"tab");tab.Tag=location;_tabs.Children.Add(tab);
        }
        Grid.SetRow(_tabs,2);main.Children.Add(_tabs);_toolbar.Margin=new(0,12,0,12);Grid.SetRow(_toolbar,3);main.Children.Add(_toolbar);
        Grid.SetRow(_body,4);main.Children.Add(_body);
        var statusRow=new Grid{ColumnDefinitions=new("*,Auto")};statusRow.Children.Add(_status);
        var edition=Text(app.IsDemo?"DEMO COLLECTION  ·  FICTIONAL ACCOUNTS":"LOCAL COLLECTION  ·  v0.15",9,true);edition.LetterSpacing=1;Grid.SetColumn(edition,1);statusRow.Children.Add(edition);
        var footer=new Border{Background=Brush("#0D0E10"),BorderBrush=Brush("#30291D"),BorderThickness=new(0,1,0,0),Padding=new(20,10),Child=statusRow};Grid.SetRow(footer,1);Grid.SetColumnSpan(footer,2);root.Children.Add(footer);Content=root;
        _timer.Tick+=(_,_)=>Refresh();_timer.Start();
        Opened+=async(_,_)=>
        {
            ArtworkReady=LoadArtwork();
            if(_app.IsDemo)_status.Text="Demo collection - game actions are disabled.";
            else await Run(async()=>{await _app.LoadWorld();_banks.ItemsSource=_app.Banks();_status.Text=_app.WorldStatus;});
            await ArtworkReady;
        };
        Closing+=OnClosing;Closed+=(_,_)=>{_timer.Stop();_images.Dispose();};
        Refresh(true);
    }
    private async Task LoadArtwork()
    {
        try{await _images.Load(_app.ClientPath);Refresh(true);}
        catch(Exception){_status.Text="Game sprites unavailable. Choose your game installation in Settings; item names remain visible.";}
    }
    private Control ItemArtwork(Item item,double size)
    {
        var source=_images.Get(item.Sprite,item.Color);
        if(source==null)return new TextBlock{Text=item.Name,FontSize=10,Foreground=Brush("#98999F"),TextWrapping=TextWrapping.Wrap,
            TextTrimming=TextTrimming.CharacterEllipsis,TextAlignment=TextAlignment.Center,MaxHeight=size,VerticalAlignment=VerticalAlignment.Center};
        var image=new Image{Source=source,MaxWidth=size,MaxHeight=size,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        RenderOptions.SetBitmapInterpolationMode(image,Avalonia.Media.Imaging.BitmapInterpolationMode.None);return image;
    }
    private static IBrush Brush(string color)=>new SolidColorBrush(Color.Parse(color));
    private static TextBlock Text(string text,double size=13,bool muted=false)=>new(){Text=text,FontSize=size,FontFamily=new(size>=20?"Georgia":"Segoe UI"),Foreground=muted?Brush("#98999F"):Brush("#F2E8D2"),TextWrapping=TextWrapping.Wrap};
    private Button Button(string text,Func<Task> action,string? style=null)
    {
        var button=new Button{Content=text};if(style!=null)button.Classes.Add(style);
        button.Click+=async(_,_)=>await Run(action);return button;
    }
    private async Task Run(Func<Task> action)
    {
        try{await action();}
        catch(OperationCanceledException){_status.Text="Operation stopped.";}
        catch(Exception ex){_status.Text=ex.Message;await Message("Action stopped",ex.Message);}
        finally{Refresh(true);}
    }
    private void Refresh(bool force=false)
    {
        if(this.GetVisualDescendants().OfType<SlotButton>().Any(x=>x.GestureActive))return;
        var characters=_app.Store.Characters();
        var key=_allAccounts+string.Join('|',characters.Select(x=>x.Name+(_app.Session(x.Name)!=null?"+":"-")));
        if(_selected==null&&characters.Count>0)_selected=characters[0].Name;
        if(force||key!=_characterKey)
        {
            _characterKey=key;_characters.Children.Clear();
            var all=Button("All accounts",()=>{_allAccounts=true;_location="All items";_search.Text="";Refresh(true);return Task.CompletedTask;},"character");
            all.HorizontalAlignment=HorizontalAlignment.Stretch;if(_allAccounts)all.Classes.Add("active");_characters.Children.Add(all);
            if(characters.Count==0)_characters.Children.Add(Text("No characters yet.\n\nLaunch a client or add a saved login.",13,true));
            foreach(var character in characters)
            {
                var online=_app.Session(character.Name)!=null;
                var panel=new Grid{ColumnDefinitions=new("34,*")};
                panel.Children.Add(new Border{Width=25,Height=29,Background=Brush("#29251D"),BorderBrush=Brush("#5E4D2E"),BorderThickness=new(1),CornerRadius=new(4),Child=new TextBlock{Text=character.Name[..1].ToUpperInvariant(),FontFamily=new("Georgia"),FontSize=15,Foreground=Brush("#D6B46A"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}});
                var info=new StackPanel{Spacing=4};info.Children.Add(Text(character.Name,14));info.Children.Add(Text(online?"●  Connected":"○  Saved collection",10,true));Grid.SetColumn(info,1);panel.Children.Add(info);
                var button=Button("",()=>{_allAccounts=false;_selected=character.Name;if(_location=="All items")_location="Inventory";_search.Text="";Refresh(true);return Task.CompletedTask;},"character");button.Tag=character.Name;button.Content=panel;
                button.HorizontalAlignment=HorizontalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Left;
                if(!_allAccounts&&_selected==character.Name)button.Classes.Add("active");_characters.Children.Add(button);
            }
        }
        var session=_app.Session(_selected);
        _title.Text=_allAccounts?"All accounts":_selected??"Your characters";
        _detail.Text=session!=null?$"{session.MapName} · {session.Position.X}, {session.Position.Y} · {session.Gold:N0} gold":_selected==null?"Launch Dark Ages to begin.":"Offline · showing last saved items";
        if(_app.Busy&&session!=null)_status.Text=session.Status;
        if(_app.QueueRunning)_status.Text=_app.QueueStatus;
        if(_launchButton!=null)_launchButton.IsEnabled=!_allAccounts&&!_app.Busy&&!_app.IsDemo;
        if(_allAccounts)_detail.Text=$"{characters.Count(x=>_app.Accounts.Displayed(x.Name))} of {characters.Count} characters displayed · saved inventory and banks";
        var items=_selected==null||_allAccounts?[]:_app.Items(_selected,_location);
        var freshness=_selected==null?"Never scanned":_app.Store.Freshness(_selected,_location);
        var aggregate=_allAccounts||!string.IsNullOrWhiteSpace(_search.Text);
        var combined=aggregate?_app.Accounts.Items(_search.Text?.Trim()??"",_allAccounts&&_location!="All items"?_location:null):null;
        var renderKey=$"{_allAccounts}/{_selected}/{_location}/{_search.Text}/{session?.Ready}/{freshness}/"+string.Join('|',items.Select(x=>$"{x.Slot}:{x.Name}:{x.Quantity}:{x.Durability}:{x.Category}"));
        if(combined!=null)renderKey+=string.Join('|',combined.Select(x=>$"{x.Character}:{x.Location}:{x.Item}:{x.Updated}"));
        if(!force&&renderKey==_renderKey)return;_renderKey=renderKey;
        foreach(var control in _tabs.Children.OfType<Button>()){control.Classes.Set("active",(string?)control.Tag==_location);control.IsVisible=(string?)control.Tag!="All items"||_allAccounts;}
        _toolbar.Children.Clear();
        if(combined!=null){RenderCombined(combined);return;}
        if(_selected==null){_body.Content=Empty("All your items, in one place","Launch a client and sign in. Inventory will appear here; bank contents are saved after a bank scan.");return;}
        if(_location=="Inventory")
        {
            _toolbar.Children.Add(Button("Sort name",()=>Sort(false)));_toolbar.Children.Add(Button("Sort category",()=>Sort(true)));
            _toolbar.Children.Add(Button("Save layout",SaveLayout));_toolbar.Children.Add(Button("Apply layout",ApplyLayout));
            _toolbar.Children.Add(Text($"{items.Length} / 59 slots",12,true));
            RenderSlots(items);
        }
        else if(_location=="Bank")
        {
            _toolbar.Children.Add(Button("Scan Bank",()=>_app.RunOperation(t=>_app.RefreshBank(RequireSession(),false,t)),"primary"));
            _toolbar.Children.Add(_banks);_toolbar.Children.Add(Button("Travel to bank",ScanBank));
            var rows=RenderRows(items.Select(x=>new StoredItem(_selected,"Bank",x,DateTimeOffset.MinValue)).ToArray(),false);
            var panel=new DockPanel();var summary=Text($"{freshness} · {items.Length:N0} entries. Refresh at banker after deposits or withdrawals.",12,true);summary.Margin=new(0,0,0,12);DockPanel.SetDock(summary,Dock.Top);panel.Children.Add(summary);panel.Children.Add(items.Length==0?Empty(freshness=="Current"?"Bank empty":"Bank not yet captured",freshness=="Current"?"No stored items found at the last bank scan.":"Choose a destination and Scan Bank, or open Withdraw Items in the game."):rows);_body.Content=panel;
        }
        else _body.Content=items.Length==0?Empty("No equipment saved","Equipment updates while character is connected."):RenderRows(items.Select(x=>new StoredItem(_selected,"Equipment",x,DateTimeOffset.MinValue)).ToArray(),false);
    }
    private static Control Empty(string title,string description)
    {
        var panel=new StackPanel{Spacing=12,MaxWidth=470,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};panel.Children.Add(Text(title,22));panel.Children.Add(Text(description,14,true));return panel;
    }
    private void RenderCombined(IReadOnlyList<StoredItem> source)
    {
        RenderItemGrids(source);
    }
    private Control RenderRows(IReadOnlyList<StoredItem> rows,bool owners)
    {
        var list=new StackPanel{Spacing=1};
        var header=new Grid{ColumnDefinitions=new(owners?"44,*,80,160,120":"44,*,85,120"),Margin=new(10,0,10,8)};
        AddCell(header,Text("ITEM",11,true),1);AddCell(header,Text("QTY",11,true),2);AddCell(header,Text(owners?"CHARACTER":"DURABILITY",11,true),3);
        if(owners)AddCell(header,Text("LOCATION",11,true),4);list.Children.Add(header);
        foreach(var row in rows)
        {
            var grid=new Grid{ColumnDefinitions=new(owners?"44,*,80,160,120":"44,*,85,120"),Margin=new(10,7)};
            var icon=_images.Get(row.Item.Sprite,row.Item.Color);if(icon!=null)grid.Children.Add(new Image{Source=icon,Width=28,Height=28});
            var description=new StackPanel{Spacing=2};description.Children.Add(Text(row.Item.Name));description.Children.Add(Text(row.Item.Category,10,true));
            AddCell(grid,description,1);AddCell(grid,Text(row.Item.Quantity.ToString("N0")),2);
            AddCell(grid,Text(owners?row.Character:row.Item.MaxDurability is >0?$"{row.Item.Durability:N0} / {row.Item.MaxDurability:N0}":"—",12,true),3);
            if(owners){var location=new StackPanel{Spacing=2};location.Children.Add(Text(row.Location,12));location.Children.Add(Text(_app.Store.Freshness(row.Character,row.Location),10,true));AddCell(grid,location,4);}
            var border=new Border{Background=Brush("#191A1E"),BorderBrush=Brush("#2E2B26"),BorderThickness=new(0,0,0,1),Child=grid,CornerRadius=new(3)};
            ToolTip.SetTip(border,owners?$"Last observed {row.Updated.LocalDateTime:g}":$"Slot {row.Item.Slot} · {row.Item.Name}");list.Children.Add(border);
        }
        return new ScrollViewer{Content=list,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    }
    private static void AddCell(Grid grid,Control control,int column){Grid.SetColumn(control,column);control.VerticalAlignment=VerticalAlignment.Center;grid.Children.Add(control);}
    private void RenderSlots(Item[] items)
    {
        var pins=_app.Pins(_selected!);var slots=items.ToDictionary(x=>x.Slot);
        var grid=new UniformGrid{Columns=12,Rows=5};
        for(var slot=1;slot<=60;slot++)
        {
            var index=slot;var item=slots.GetValueOrDefault(slot);
            var locked=pins.Contains(slot);
            var button=new SlotButton{CanDrag=item!=null&&slot<60&&!locked,MinHeight=48,Tag=slot,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};button.Classes.Add("slot");
            if(locked)button.Classes.Add("protected");
            var content=new Grid{RowDefinitions=new("14,*,15")};
            content.Children.Add(Text(slot==60?"GOLD":$"{slot:00}"+(!locked&&pins.Contains(slot)?" •":""),10,true));
            if(locked){var marker=Text("PIN",8,true);Grid.SetRow(marker,2);content.Children.Add(marker);}
            if(slot==60)
            {
                var amount=(_app.Session(_selected)?.Gold??_app.Store.Get<uint>("gold/"+_selected!.ToLowerInvariant())).ToString("N0");
                var gold=new Viewbox{Child=Text(amount,11),Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Left};
                Grid.SetRow(gold,1);content.Children.Add(gold);ToolTip.SetTip(button,amount+" gold");
            }
            else if(item!=null)
            {
                var transfer=Text("?",10,true);transfer.HorizontalAlignment=HorizontalAlignment.Right;
                ToolTip.SetTip(transfer,"Drop / trade: unknown. The server does not include this permission in inventory data.");content.Children.Add(transfer);
                var icon=ItemArtwork(item,34);Grid.SetRow(icon,1);content.Children.Add(icon);
                var count=Text(item.Quantity>1?item.Quantity.ToString("N0"):"",10);count.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetRow(count,2);content.Children.Add(count);
                ToolTip.SetTip(button,$"{item.Name}\n{ItemCategories.Label(item)}\nQuantity: {item.Quantity:N0}\nDrop / trade: unknown\nSlot {slot}"+(locked?" · pinned":pins.Contains(slot)?" · pinned":""));
            }

            button.Content=content;
            if(slot<60)
            {
                button.Click+=async(_,_)=>{if(item!=null)await Run(()=>ItemDetail(item));};
                var pin=new MenuItem{Header=pins.Contains(slot)?"Unpin slot":"Pin slot"};pin.Click+=(_,_)=>{_app.TogglePin(_selected!,index);Refresh(true);};button.ContextMenu=new(){ItemsSource=new[]{pin}};
                WireDrag(button,index,item);
            }
            grid.Children.Add(button);
        }
        var panel=new DockPanel();var guide=new StackPanel{Spacing=10,Margin=new(3,16,3,0)};
        guide.Children.Add(new CelticRule{Height=8,Opacity=.6});
        guide.Children.Add(Text("Quick potions fill from the left; trinkets fill from the right. Carried Dual Crystal Arrows take slot 1.",11,true));
        guide.Children.Add(Text("Drag to move. Right-click to pin or unpin any slot. Manual pins override sorting preferences.",11,true));
        DockPanel.SetDock(guide,Dock.Bottom);panel.Children.Add(guide);panel.Children.Add(grid);_body.Content=panel;
    }
    private void WireDrag(SlotButton button,int slot,Item? item)
    {
        var character=_selected;
        button.DroppedOn+=async target=>
        {
            if(target.Tag is not int destination||destination==60)return;
            await Run(async()=>
            {
                if(_selected!=character)throw new InvalidOperationException("Character changed. Try the move again.");
                var session=RequireSession();var pins=_app.Pins(character!);
                if(pins.Contains(slot)||pins.Contains(destination))throw new InvalidOperationException("Unpin both slots before moving.");
                var inventory=session.Inventory();
                var actual=inventory.FirstOrDefault(x=>x.Slot==slot);
                if(item==null||actual==null||(item with{Category=actual.Category})!=actual)
                    throw new InvalidOperationException("Inventory changed. Try the move again.");
                var desired=inventory.ToDictionary(x=>x.Slot);
                desired.Remove(slot,out var a);desired.Remove(destination,out var b);
                desired[destination]=a!;if(b!=null)desired[slot]=b;
                await _app.RunOperation(t=>session.ApplyLayout(desired,t));
            });
        };
    }
    private GameSession RequireSession()=>_app.Session(_selected)??throw new InvalidOperationException("Launch and log in this character first.");
}
