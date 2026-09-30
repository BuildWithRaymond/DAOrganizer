using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DAOrganizer.App;
using DAOrganizer.Core;

var output=Path.GetFullPath(args.FirstOrDefault()??"artifacts/ui-checks");
Directory.CreateDirectory(output);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new(){UseHeadlessDrawing=false}).SetupWithoutStarting();
if(args.Contains("--gallery")){Gallery.Run(output,args.Contains("--require-sprites"));return;}
// A new profile keeps previous rule/filter checks from contaminating a repeated run.
using var app=new Organizer(Path.Combine(output,"profiles",Guid.NewGuid().ToString("N")));
app.Store.SaveSnapshot("Example","Inventory",[
    new(9,"Hy-brasyl Sword",1,1,Durability:9400,MaxDurability:10000),
    new(1,"Komadium",10,5),new(2,"Red Potion",20,5),new(3,"Exkuranum",15,5),new(4,"Dibenomum",5,5),new(5,"Hemloch",30,5),
    new(8,"Wolf Pelt",7,12),new(20,"Emerald",9,15)],true);
app.Store.SaveSnapshot("Example","Equipment",[new(2,"Leather Armor",1,12,Durability:600,MaxDurability:1000)],true);
app.Store.SaveSnapshot("Example","Bank",Enumerable.Range(1,80).Select(i=>new Item(i,$"Stored item {i:000}",i*3)),true);
app.Store.SaveSnapshot("Storage","Inventory",[new(1,"Emerald",30,15)],true);
app.Store.Put("pins/example",new[]{9});
app.Accounts.SetDisplay("Example",true);app.Accounts.SetDisplay("Storage",true);
var window=new MainWindow(app);window.Show();
for(var i=0;i<100;i++){Dispatcher.UIThread.RunJobs();Thread.Sleep(50);}
void Capture(string name,Window? target=null)
{
    Dispatcher.UIThread.RunJobs();
    using var frame=(target??window).CaptureRenderedFrame()??throw new Exception("No rendered frame");
    frame.Save(Path.Combine(output,name+".png"));
    Console.WriteLine($"Rendered {name}: {frame.PixelSize}");
}
void Click(string label)
{
    var button=window.GetVisualDescendants().OfType<Button>().First(b=>b.Content as string==label);
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
}
Capture("inventory");
var slots=window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.UniformGrid>().Single();
if(slots.Columns!=12||slots.Rows!=5||slots.Children.Count!=60)throw new Exception("Inventory geometry must match the game: 12 × 5.");
if(slots.Children.Take(5).Cast<SlotButton>().Any(x=>!x.CanDrag))throw new Exception("Unpinned potions must be draggable.");
if(window.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text=="LOCK"))throw new Exception("Potions must not show forced locks.");
void ToggleFirstPin(string label)
{
    var first=window.GetVisualDescendants().OfType<SlotButton>().Single(x=>x.Tag is int i&&i==1);
    first.ContextMenu!.Items.OfType<MenuItem>().Single(x=>x.Header as string==label).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    Dispatcher.UIThread.RunJobs();
}
ToggleFirstPin("Pin slot");
if(window.GetVisualDescendants().OfType<SlotButton>().Single(x=>x.Tag is int i&&i==1).CanDrag)throw new Exception("Pinned potion must respect the user's pin.");
ToggleFirstPin("Unpin slot");
if(!window.GetVisualDescendants().OfType<SlotButton>().Single(x=>x.Tag is int i&&i==1).CanDrag)throw new Exception("Unpin must restore potion dragging.");
slots=window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.UniformGrid>().Single();
var source=slots.Children[7];var target=slots.Children[10];
var from=source.TranslatePoint(new Point(source.Bounds.Width/2,source.Bounds.Height/2),window)!.Value;
var to=target.TranslatePoint(new Point(target.Bounds.Width/2,target.Bounds.Height/2),window)!.Value;
window.MouseDown(from,Avalonia.Input.MouseButton.Left);
Dispatcher.UIThread.RunJobs();
if(!((SlotButton)source).GestureActive)throw new Exception("Item did not receive pointer press.");
window.MouseMove(to,RawInputModifiers.LeftMouseButton);
Dispatcher.UIThread.RunJobs();
window.MouseUp(to,Avalonia.Input.MouseButton.Left);
Dispatcher.UIThread.RunJobs();
var dragMessage=window.OwnedWindows.SingleOrDefault(x=>x.Title=="Action stopped");
if(dragMessage==null)throw new Exception("Real pointer drag did not reach move handler (offline guard expected).");
dragMessage.Close();Console.WriteLine("Real pointer drag reached move handler.");
// A click remains a details action, and dragging off the grid cancels without a move.
Dispatcher.UIThread.RunJobs();
slots=window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.UniformGrid>().Single();
source=slots.Children[7];from=source.TranslatePoint(new Point(source.Bounds.Width/2,source.Bounds.Height/2),window)!.Value;
window.MouseDown(from,MouseButton.Left);window.MouseMove(new Point(230,400),RawInputModifiers.LeftMouseButton);window.MouseUp(new Point(230,400),MouseButton.Left);
Dispatcher.UIThread.RunJobs();
if(window.OwnedWindows.Any())throw new Exception("Drag outside inventory must cancel.");
window.MouseDown(from,MouseButton.Left);window.MouseUp(from,MouseButton.Left);Dispatcher.UIThread.RunJobs();
var details=window.OwnedWindows.Single(x=>x.Title=="Wolf Pelt");details.Close();
Click("Bank");Capture("bank");
Click("Equipment");Capture("equipment");
window.GetVisualDescendants().OfType<TextBox>().First().Text="Emerald";
Capture("search");
window.GetVisualDescendants().OfType<TextBox>().First().Text="";Click("Inventory");
window.Width=980;window.Height=720;Capture("inventory-small");
Click("All accounts");Capture("all-accounts");
var emerald=window.GetVisualDescendants().OfType<Button>().Single(x=>x.Tag is ItemGroup g&&g.Item.Name=="Emerald");
if(((ItemGroup)emerald.Tag!).Quantity!=39)throw new Exception("Grouped total must be 39.");
emerald.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
for(var i=0;i<8;i++){Dispatcher.UIThread.RunJobs();Thread.Sleep(40);}
Capture("item-drawer-small");
var rule=window.GetVisualDescendants().OfType<ComboBox>().Single();rule.SelectedIndex=1;Dispatcher.UIThread.RunJobs();
if(app.Rules.Get(new(1,"Emerald",1,15))!=ItemAction.Junk)throw new Exception("Junk rule did not persist.");
Click("Junk cleanout");
var review=window.OwnedWindows.Single(x=>x.Title=="Review junk cleanout");Capture("junk-preview",review);
if(review.GetVisualDescendants().OfType<CheckBox>().Count()!=2)throw new Exception("Preview should include both owners.");
review.GetVisualDescendants().OfType<Button>().Single(x=>x.Content as string=="Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
if(app.Busy||app.Sessions.Count!=0)throw new Exception("Cancelled preview must not launch or mutate game sessions.");
window.Width=1180;window.Height=810;Capture("item-drawer");
rule=window.GetVisualDescendants().OfType<ComboBox>().Single();rule.SelectedIndex=2;Dispatcher.UIThread.RunJobs();
if(app.PlanMaintenance(ItemAction.AutoDeposit).Sum(x=>x.Item.Quantity)!=39)throw new Exception("Auto-deposit plan incorrect.");
Click("Close details");Capture("all-accounts-wide");
// Opening details from deep in the grid must keep the clicked item in view,
// including when the drawer changes how many tiles fit on a row.
Button ItemTile(string name)=>window.GetVisualDescendants().OfType<Button>().Single(x=>x.Tag is ItemGroup g&&g.Item.Name==name);
ScrollViewer ItemScroll()=>ItemTile("Stored item 070").FindAncestorOfType<ScrollViewer>()!;
void Settle(){for(var i=0;i<8;i++){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Thread.Sleep(40);}}
var scroll=ItemScroll();ItemTile("Stored item 070").BringIntoView();Settle();
if(scroll.Offset.Y<100)throw new Exception("Scroll regression fixture must start below the top.");
var itemTile=ItemTile("Stored item 070");var oldY=itemTile.TranslatePoint(default,scroll)!.Value.Y;
var itemPoint=itemTile.TranslatePoint(new Point(itemTile.Bounds.Width/2,itemTile.Bounds.Height/2),window)!.Value;
window.MouseDown(itemPoint,MouseButton.Left);window.MouseUp(itemPoint,MouseButton.Left);Settle();
scroll=ItemScroll();var newY=ItemTile("Stored item 070").TranslatePoint(default,scroll)!.Value.Y;
if(scroll.Offset.Y<100||Math.Abs(oldY-newY)>2)throw new Exception($"Opening details moved the clicked item: {oldY} -> {newY}, offset {scroll.Offset.Y}.");
Capture("scrolled-item-details");
var drawerClose=window.GetVisualDescendants().OfType<Button>().Single(x=>x.Content as string=="Close details");
if(drawerClose.GetVisualAncestors().Any(x=>x.Opacity<.99))throw new Exception("Item drawer did not finish appearing.");
var beforeRule=scroll.Offset.Y;
window.GetVisualDescendants().OfType<ComboBox>().Single().SelectedIndex=1;
for(var i=0;i<15;i++){Dispatcher.UIThread.RunJobs();Thread.Sleep(50);}
if(Math.Abs(ItemScroll().Offset.Y-beforeRule)>2)throw new Exception("Rule refresh lost scroll position.");
// Closing the drawer also preserves the first visible tile through reflow.
scroll=ItemScroll();
var visible=scroll.GetVisualDescendants().OfType<Button>().Where(x=>x.Tag is ItemGroup)
    .Select(x=>(Tile:x,Y:x.TranslatePoint(default,scroll)!.Value.Y)).First(x=>x.Y>=0&&x.Y<scroll.Viewport.Height);
var visibleName=((ItemGroup)visible.Tile.Tag!).Item.Name;
Click("Close details");Settle();scroll=ItemScroll();
if(scroll.Offset.Y<100||Math.Abs(ItemTile(visibleName).TranslatePoint(default,scroll)!.Value.Y-visible.Y)>2)
    throw new Exception("Closing details lost the visible item position.");
Capture("scrolled-details-closed");
Console.WriteLine("Scrolled item opening, rule refresh and drawer closing preserved position.");
window.GetVisualDescendants().OfType<TextBox>().First().Text="No such item";Capture("empty-search");
window.GetVisualDescendants().OfType<TextBox>().First().Text="";
Settle();if(ItemScroll().Offset.Y>1)throw new Exception("New search should start at the top.");
Click("Characters…");
var filter=window.OwnedWindows.Single(x=>x.Title=="Displayed characters");Capture("display-characters",filter);
filter.GetVisualDescendants().OfType<CheckBox>().Single(x=>x.Content as string=="Storage").IsChecked=false;
filter.Close();Capture("all-accounts-filtered");
if(app.Accounts.Items("").Count!=89)throw new Exception("Display filtering failed.");
if(app.Store.Items("Storage","Inventory").Count!=1)throw new Exception("Hiding must not remove history.");
app.Store.SaveSnapshot("Example","Bank",app.Store.Items("Example","Bank").Append(new Item(81,"Water Dungeon Chest",1,15,IsStackable:true)),true);
app.Store.SaveSnapshot("Storage","Bank",[new Item(1,"Water Dungeon Chest",1,15,IsStackable:true)],true);
var storageRole=app.Accounts.AddStorageRole("Storage","Chests");
app.Accounts.AddStorageRule(storageRole.Id,StorageMatchKind.Item,ItemGroups.Key(new Item(1,"Water Dungeon Chest",1,15)));
Click("Account manager");
var manager=window.OwnedWindows.Single(x=>x.Title=="Account manager");Capture("account-manager",manager);
manager.GetVisualDescendants().OfType<Button>().First(x=>x.Content as string=="Game accounts & storage")
    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
var storage=manager.OwnedWindows.Single(x=>x.Title=="Game accounts & storage");
storage.GetVisualDescendants().OfType<ComboBox>().First().SelectedItem="Storage";Dispatcher.UIThread.RunJobs();
Capture("storage-setup",storage);storage.Close();manager.Close();
Click("Organization plan");
var organization=window.OwnedWindows.Single(x=>x.Title=="Organization plan");Capture("organization-plan",organization);organization.Close();
window.Close();
Console.WriteLine("UI checks completed. Synthetic data only.");
