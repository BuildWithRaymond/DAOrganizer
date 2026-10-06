using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DAOrganizer.Core;
namespace DAOrganizer.App;

public sealed partial class MainWindow
{
    private string? _selectedItemKey;
    private bool _animateDrawer;
    private ScrollViewer? _itemScroll;
    private string _itemScrollContext="";
    private string? _itemAnchorKey;
    private void RenderItemGrids(IReadOnlyList<StoredItem> source)
    {
        var context=$"{_allAccounts}/{_selected}/{_location}/{_search.Text}";
        var sameView=context==_itemScrollContext&&_itemScroll?.GetVisualRoot()!=null;
        var offset=sameView?_itemScroll!.Offset:default;
        string? anchorKey=null;double anchorY=0;
        if(sameView)
        {
            var previous=_itemScroll!;
            var visible=previous.GetVisualDescendants().OfType<Button>().Where(x=>x.Tag is ItemGroup)
                .Select(x=>(Key:((ItemGroup)x.Tag!).Key,Y:x.TranslatePoint(default,previous)!.Value.Y)).ToArray();
            var anchor=_itemAnchorKey!=null?visible.FirstOrDefault(x=>x.Key==_itemAnchorKey):
                visible.FirstOrDefault(x=>x.Y>=0&&x.Y<previous.Viewport.Height);
            anchorKey=anchor.Key;anchorY=anchor.Y;
        }
        _itemAnchorKey=null;_itemScrollContext=context;_itemScroll=null;
        var groups=ItemGroups.Build(source);
        _toolbar.Children.Add(Button("Characters…",DisplayCharacters));
        _toolbar.Children.Add(Button("Junk cleanout",()=>PreviewMaintenance(ItemAction.Junk),"danger"));
        _toolbar.Children.Add(Button("Deposit marked",()=>PreviewMaintenance(ItemAction.AutoDeposit),"primary"));
        _toolbar.Children.Add(Button("Results",MaintenanceHistory,"quiet"));
        var count=Text($"{groups.Count:N0} unique  ·  {groups.Sum(x=>x.Quantity):N0} total",11,true);count.VerticalAlignment=VerticalAlignment.Center;count.Margin=new(6,0);_toolbar.Children.Add(count);
        if(groups.Count==0){_body.Content=Empty("No items to display","Try another search or include more characters. Scan accounts to capture their inventory and banks.");return;}
        var selected=groups.FirstOrDefault(x=>x.Key==_selectedItemKey);
        var root=new Grid{ColumnDefinitions=new(selected==null?"*":"*,310"),ClipToBounds=true};
        var sections=new StackPanel{Spacing=22,Margin=new(0,0,selected==null?6:18,16)};
        foreach(var category in groups.GroupBy(x=>x.Item.Category))
        {
            var section=new StackPanel{Spacing=12};
            var header=new Grid{ColumnDefinitions=new("Auto,Auto,*,Auto"),Height=32};
            header.Children.Add(new CelticSeal{Width=23,Height=23,Margin=new(0,0,10,0)});
            var heading=Text(category.Key,19);heading.FontFamily=new("Georgia");AddCell(header,heading,1);
            AddCell(header,new Border{Height=1,Background=Brush("#393124"),Margin=new(18,0)},2);
            AddCell(header,Text($"{category.Count():00} ITEMS",9,true),3);section.Children.Add(header);
            var tiles=new WrapPanel{Orientation=Orientation.Horizontal};
            foreach(var group in category)
            {
                var tile=Button("",()=>{_itemAnchorKey=group.Key;_animateDrawer=_selectedItemKey!=group.Key;_selectedItemKey=group.Key;return Task.CompletedTask;});
                tile.Classes.Add("item-tile");tile.Tag=group;tile.Width=124;tile.Height=138;tile.Margin=new(0,0,8,8);tile.Padding=new(10,8);
                tile.HorizontalContentAlignment=HorizontalAlignment.Stretch;tile.VerticalContentAlignment=VerticalAlignment.Stretch;
                if(group.Key==_selectedItemKey)tile.Classes.Add("active");
                var content=new Grid{RowDefinitions=new("14,48,*,14")};
                var family=Text(ItemCategories.Family(group.Item).ToUpperInvariant(),8,true);family.TextTrimming=TextTrimming.CharacterEllipsis;family.TextWrapping=TextWrapping.NoWrap;family.LetterSpacing=.7;content.Children.Add(family);
                var artwork=ItemArtwork(group.Item,38);artwork.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetRow(artwork,1);content.Children.Add(artwork);
                var badge=new Border{Background=Brush("#D6B46A"),CornerRadius=new(9),Padding=new(5,1),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,
                    Child=new TextBlock{Text=group.Quantity.ToString("N0"),FontFamily=new("Consolas"),FontSize=10,Foreground=Brush("#17130B"),FontWeight=FontWeight.SemiBold}};
                ToolTip.SetTip(badge,$"{group.Quantity:N0} total in displayed locations");Grid.SetRow(badge,1);content.Children.Add(badge);
                var name=Text(group.Item.Name,11);name.MaxHeight=32;name.TextTrimming=TextTrimming.CharacterEllipsis;name.TextAlignment=TextAlignment.Center;Grid.SetRow(name,2);content.Children.Add(name);
                var owners=group.Owners.Select(x=>x.Character).Distinct().Count();
                var rule=_app.Rules.Get(group.Item);var hint=Text(rule==ItemAction.Junk?"JUNK":rule==ItemAction.AutoDeposit?"AUTO-DEPOSIT":$"{owners} {(owners==1?"owner":"owners")}",9,true);
                if(rule!=ItemAction.Keep)hint.Foreground=Brush(rule==ItemAction.Junk?"#DC8989":"#D6B46A");
                hint.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetRow(hint,3);content.Children.Add(hint);tile.Content=content;
                ToolTip.SetTip(tile,$"{group.Item.Name}\n{ItemCategories.Label(group.Item)}\n{group.Quantity:N0} total · click for owners and item rules");tiles.Children.Add(tile);
            }
            section.Children.Add(tiles);sections.Children.Add(section);
        }
        var scroll=new ScrollViewer{Content=sections,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        _itemScroll=scroll;root.Children.Add(scroll);
        if(sameView)
        {
            // Restore after layout establishes the new scroll extent. The drawer can
            // change the column count, so keep the visible tile at its previous height.
            EventHandler? restore=null;
            restore=(_,_)=>
            {
                if(scroll.Viewport.Height<=0)return;
                root.LayoutUpdated-=restore;
                if(!ReferenceEquals(_body.Content,root))return;
                var anchor=scroll.GetVisualDescendants().OfType<Button>().FirstOrDefault(x=>x.Tag is ItemGroup g&&g.Key==anchorKey);
                var y=anchor?.TranslatePoint(default,scroll)?.Y;
                scroll.Offset=new Vector(offset.X,y.HasValue?scroll.Offset.Y+y.Value-anchorY:offset.Y);
            };
            root.LayoutUpdated+=restore;
        }
        if(selected!=null)
        {
            var drawer=ItemDrawer(selected);Grid.SetColumn(drawer,1);root.Children.Add(drawer);
            if(_animateDrawer)
            {
                _animateDrawer=false;drawer.Margin=new(30,0,-30,0);drawer.Opacity=0;
                drawer.Transitions=new(){new ThicknessTransition{Property=MarginProperty,Duration=TimeSpan.FromMilliseconds(160)},new DoubleTransition{Property=OpacityProperty,Duration=TimeSpan.FromMilliseconds(160)}};
                Dispatcher.UIThread.Post(()=>{drawer.Margin=new(0);drawer.Opacity=1;},DispatcherPriority.Loaded);
            }
        }
        _body.Content=root;
    }
    private Border ItemDrawer(ItemGroup group)
    {
        var panel=new StackPanel{Spacing=16,Margin=new(20)};
        var close=Button("Close details",()=>{_selectedItemKey=null;return Task.CompletedTask;},"quiet");close.HorizontalAlignment=HorizontalAlignment.Right;panel.Children.Add(close);
        panel.Children.Add(new Border{Height=88,Background=Brush("#211E17"),BorderBrush=Brush("#4C4029"),BorderThickness=new(1),CornerRadius=new(5),Child=ItemArtwork(group.Item,54)});
        panel.Children.Add(Text(group.Item.Name,21));panel.Children.Add(Text($"{group.Quantity:N0} total · {ItemCategories.Label(group.Item)}",13,true));
        panel.Children.Add(new CelticRule{Height=10,Opacity=.6});
        panel.Children.Add(RuleEditor(group.Item));
        var consolidate=Button("Consolidate to bank",()=>ConsolidateItem(group),"primary");
        consolidate.IsEnabled=!_app.IsDemo&&_app.Store.Search("").Where(x=>ItemGroups.Key(x.Item)==group.Key)
            .Select(x=>x.Character).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1;
        panel.Children.Add(consolidate);
        if(_app.IsDemo)panel.Children.Add(Text("Consolidation is available in your live profile.",11,true));
        panel.Children.Add(Text("WHO HAS IT",11,true));
        foreach(var owner in group.Owners)
        {
            var line=new Grid{ColumnDefinitions=new("*,Auto"),Margin=new(0,4)};
            var description=new StackPanel{Spacing=4};description.Children.Add(Text(owner.Character,14));
            description.Children.Add(Text($"{owner.Location} · slot {owner.Item.Slot}\n{_app.Store.Freshness(owner.Character,owner.Location)} · {owner.Updated.LocalDateTime:g}",11,true));
            line.Children.Add(description);AddCell(line,Text($"×{owner.Item.Quantity:N0}",14),1);panel.Children.Add(line);
        }
        return new Border{Background=Brush("#131416"),BorderBrush=Brush("#78633B"),BorderThickness=new(1,0,0,0),Child=new ScrollViewer{Content=panel,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}};
    }
    private async Task ConsolidateItem(ItemGroup group)
    {
        if(_app.Busy)throw new InvalidOperationException("Stop the current operation first.");
        var characters=_app.Store.Characters().Select(x=>x.Name).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var window=DialogWindow("Consolidate to bank",620);var panel=DialogPanel();
        panel.Children.Add(Text("Consolidate to one bank",22));
        panel.Children.Add(Text(group.Item.Name,17));
        panel.Children.Add(Text("Choose the character whose bank should hold every eligible copy. DAOrganizer will handle login, travel, exchange, banking, and verification.",12,true));
        var destination=new ComboBox{ItemsSource=characters,Width=260,PlaceholderText="Destination character"};
        var preferred=_app.Store.GetItemOverride(group.Item)?.DestinationCharacter;
        destination.SelectedItem=characters.FirstOrDefault(x=>x.Equals(preferred,StringComparison.OrdinalIgnoreCase))??
            group.Owners.Where(x=>x.Location=="Bank").GroupBy(x=>x.Character,StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x=>x.Sum(y=>y.Item.Quantity)).Select(x=>x.Key).FirstOrDefault();
        panel.Children.Add(destination);
        var summaryPanel=new StackPanel{Spacing=5};panel.Children.Add(summaryPanel);
        ConsolidationSummary? summary=null;
        var confirm=new Button{Content="Consolidate",IsEnabled=false};confirm.Classes.Add("primary");
        void RenderSummary()
        {
            summaryPanel.Children.Clear();summary=null;confirm.IsEnabled=false;
            if(destination.SelectedItem is not string chosen)return;
            summary=_app.BuildConsolidationSummary(group.Item,chosen);
            summaryPanel.Children.Add(Text($"Destination bank: {chosen} · currently {summary.DestinationBankQuantity:N0}",14));
            summaryPanel.Children.Add(Text("Sources",11,true));
            foreach(var source in summary.Sources)
            {
                summaryPanel.Children.Add(Text($"{source.Character}: {source.EligibleQuantity:N0} eligible"+
                    (source.ProtectedQuantity>0?$" · {source.ProtectedQuantity:N0} protected and staying put":""),12));
            }
            summaryPanel.Children.Add(Text($"Total to move: {summary.EligibleQuantity:N0} {summary.Item.Name}",14));
            if(summary.Blocker is { } blocker)summaryPanel.Children.Add(Text("Cannot start: "+blocker,12,true));
            else summaryPanel.Children.Add(Text("After confirmation this runs hands off. If any result is uncertain, it stops without retrying and shows the last verified holder.",11,true));
            confirm.IsEnabled=summary.CanStart;
        }
        destination.SelectionChanged+=(_,_)=>RenderSummary();RenderSummary();
        var accepted=false;confirm.Click+=(_,_)=>{accepted=true;window.Close();};
        var cancel=new Button{Content="Cancel"};cancel.Click+=(_,_)=>window.Close();
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};actions.Children.Add(confirm);actions.Children.Add(cancel);panel.Children.Add(actions);
        window.Content=panel;await window.ShowDialog(this);
        if(!accepted||summary is null)return;
        await ShowConsolidationProgress(summary);
    }

    private async Task ShowConsolidationProgress(ConsolidationSummary summary)
    {
        var window=DialogWindow("Consolidating "+summary.Item.Name,620);var panel=DialogPanel();
        panel.Children.Add(Text("Consolidating to "+summary.DestinationCharacter+"'s bank",22));
        var character=Text("Preparing",15);var action=Text("Preflight complete",13,true);
        var completed=Text($"0 of {summary.EligibleQuantity:N0} transfers verified",13);
        var blocker=Text("",12,true);panel.Children.Add(character);panel.Children.Add(action);panel.Children.Add(completed);panel.Children.Add(blocker);
        var ended=false;
        var stop=Button("Stop safely",()=>
        {
            if(!ended)_app.Stop();
            else
            {
                var holder=_app.ConsolidationStatus?.LastVerifiedHolder;
                window.Close();
                if(!string.IsNullOrWhiteSpace(holder))
                {
                    _allAccounts=false;_selected=holder;_location="Inventory";_search.Text="";Refresh(true);
                }
            }
            return Task.CompletedTask;
        },"danger");panel.Children.Add(stop);
        void Update(ConsolidationProgress progress)=>Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            character.Text="Current character: "+progress.CurrentCharacter;
            action.Text="Current action: "+progress.CurrentAction;
            completed.Text=$"{progress.CompletedTransfers:N0} of {progress.TotalTransfers:N0} transfers verified";
            blocker.Text=progress.Blocker is null?"":$"Blocked: {progress.Blocker}\nLast verified holder: {progress.LastVerifiedHolder}";
            if(progress.Finished)stop.Content="Close";
        });
        _app.ConsolidationChanged+=Update;window.Content=panel;window.Show(this);
        try
        {
            await _app.RunOperation(t=>_app.ConsolidateToBank(summary.Item,summary.DestinationCharacter,t));
        }
        catch(OperationCanceledException){/* Progress already identifies the safe stop. */}
        catch(Exception){/* The progress view is the single player-facing blocker. */}
        finally
        {
            _app.ConsolidationChanged-=Update;ended=true;
            stop.Content=_app.ConsolidationStatus?.Blocker is null?"Close":"Inspect last verified holder";
            stop.IsEnabled=true;
        }
    }
    private Control RuleEditor(Item item)
    {
        var panel=new StackPanel{Spacing=8};panel.Children.Add(Text("ITEM RULE · ALL CHARACTERS",10,true));
        var choices=new ComboBox{ItemsSource=new[]{"Keep","Junk","Auto-deposit"},SelectedIndex=(int)_app.Rules.Get(item),HorizontalAlignment=HorizontalAlignment.Stretch};
        var note=Text("",11,true);
        void Explain()=>note.Text=_app.Rules.Get(item) switch
        {
            ItemAction.Junk=>"Included in Junk cleanout. One drop attempt; rejected items stay in inventory. Review before running.",
            ItemAction.AutoDeposit=>"Bank during account updates. Deposit marked runs it now. Pinned slots and equipped items stay put.",
            _=>"Keep in place. No cleanup or automatic deposit."
        };
        choices.SelectionChanged+=(_,_)=>{if(choices.SelectedIndex<0)return;_app.Rules.Set(item,(ItemAction)choices.SelectedIndex);_renderKey="";Explain();};
        Explain();panel.Children.Add(choices);panel.Children.Add(note);return panel;
    }
    private async Task PreviewMaintenance(ItemAction action)
    {
        if(_app.Busy)throw new InvalidOperationException("Stop the current operation first.");
        var plan=_app.PlanMaintenance(action);
        var title=action==ItemAction.Junk?"Review junk cleanout":"Review auto-deposit";
        var window=DialogWindow(title,670);var panel=DialogPanel();
        panel.Children.Add(Text(title,22));
        panel.Children.Add(Text($"{plan.Count:N0} stacks · {plan.Select(x=>x.Character).Distinct().Count()} characters · all saved accounts, including hidden characters",12,true));
        panel.Children.Add(Text(action==ItemAction.Junk?"Selected items will be dropped on the ground and may be lost. Bank items are withdrawn first. Failed drops get no retry; that item stays in inventory.":"Only items marked Auto-deposit will be banked. Equipped items and pinned inventory slots are excluded.",13,true));
        var rows=new StackPanel{Spacing=7};var selections=new List<(CheckBox Box,MaintenanceEntry Entry)>();
        foreach(var entry in plan)
        {
            var check=new CheckBox{IsChecked=true,Content=$"{entry.Character} · {entry.Location} · {entry.Item.Name} ×{entry.Item.Quantity:N0}"};
            rows.Children.Add(check);selections.Add((check,entry));
        }
        if(plan.Count==0)rows.Children.Add(Text("No eligible items. Choose an item and set its rule first.",14));
        panel.Children.Add(new ScrollViewer{Content=rows,MaxHeight=340,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        var run=new Button{Content=action==ItemAction.Junk?"Drop selected junk":"Deposit selected",IsEnabled=plan.Count>0};run.Classes.Add("primary");
        IReadOnlyList<MaintenanceEntry> accepted=[];
        run.Click+=(_,_)=>{accepted=selections.Where(x=>x.Box.IsChecked==true).Select(x=>x.Entry).ToArray();window.Close();};
        var cancel=new Button{Content="Cancel"};cancel.Click+=(_,_)=>window.Close();actions.Children.Add(run);actions.Children.Add(cancel);panel.Children.Add(actions);
        window.Content=panel;await window.ShowDialog(this);
        if(accepted.Count>0)
        {
            try{await _app.RunMaintenance(accepted);}
            finally{await MaintenanceHistory();}
        }
    }
    private async Task MaintenanceHistory()
    {
        var results=_app.Store.Get<List<MaintenanceResult>>("maintenanceResults")??[];
        var window=DialogWindow("Item maintenance results",650);var panel=DialogPanel();panel.Children.Add(Text("Item maintenance results",22));
        var rows=new StackPanel{Spacing=12};
        foreach(var row in results){var line=new StackPanel{Spacing=3};line.Children.Add(Text($"{row.Character} · {row.Item}"));line.Children.Add(Text(row.Result,12,true));rows.Children.Add(line);}
        if(results.Count==0)rows.Children.Add(Text("No item maintenance has run yet.",14,true));
        panel.Children.Add(new ScrollViewer{Content=rows,MaxHeight=450});var close=new Button{Content="Close"};close.Click+=(_,_)=>window.Close();panel.Children.Add(close);window.Content=panel;await window.ShowDialog(this);
    }
}
