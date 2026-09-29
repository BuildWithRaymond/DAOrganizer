using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using DAOrganizer.Core;
using DAOrganizer.Game;
namespace DAOrganizer.App;

public sealed partial class MainWindow
{
    private async Task DisplayCharacters()
    {
        var window=DialogWindow("Displayed characters",430);var panel=DialogPanel();
        panel.Children.Add(Text("Include in All accounts and search",18));
        panel.Children.Add(Text("Unchecking a character hides its items here. Saved inventory and bank history remain available.",12,true));
        var choices=new StackPanel{Spacing=8};
        void Render()
        {
            choices.Children.Clear();
            foreach(var character in _app.Store.Characters())
            {
                var check=new CheckBox{Content=character.Name,IsChecked=_app.Accounts.Displayed(character.Name)};
                check.IsCheckedChanged+=(_,_)=>{_app.Accounts.SetDisplay(character.Name,check.IsChecked==true);Refresh(true);};choices.Children.Add(check);
            }
        }
        var controls=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        foreach(var include in new[]{true,false})controls.Children.Add(Button(include?"Select all":"Select none",()=>
        {
            foreach(var character in _app.Store.Characters())_app.Accounts.SetDisplay(character.Name,include);
            Render();Refresh(true);return Task.CompletedTask;
        }));
        panel.Children.Add(controls);panel.Children.Add(new ScrollViewer{MaxHeight=400,Content=choices});Render();
        var done=new Button{Content="Done"};done.Click+=(_,_)=>window.Close();panel.Children.Add(done);
        window.Content=panel;await window.ShowDialog(this);
    }
    private async Task AccountManager()
    {
        var window=DialogWindow("Account manager",960);var panel=DialogPanel();
        panel.Children.Add(Text("Account manager",23));
        panel.Children.Add(Text("One pass: log in → nearby NPC → save items → safe logout. Bank reads stay at the inn; marked deposits travel to a bank. Failed clients stay open. Existing connected characters are skipped.",13,true));
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        var table=new StackPanel{Spacing=5};var message=Text(_app.QueueStatus,12,true);
        var resultLabels=new Dictionary<string,TextBlock>(StringComparer.OrdinalIgnoreCase);
        var editable=new List<Control>();
        void Render()
        {
            table.Children.Clear();resultLabels.Clear();editable.Clear();
            var header=new Grid{ColumnDefinitions=new("65,65,130,85,*,170"),Margin=new(8,0,8,4)};
            foreach(var cell in new[]{("SHOW",0),("UPDATE",1),("CHARACTER",2),("LOGIN",3),("LAST RESULT",4)})AddCell(header,Text(cell.Item1,10,true),cell.Item2);
            table.Children.Add(header);
            foreach(var character in _app.Store.Characters())
            {
                var name=character.Name;var row=new Grid{ColumnDefinitions=new("65,65,130,85,*,170"),Margin=new(8,6)};
                var display=new CheckBox{IsChecked=_app.Accounts.Displayed(name)};
                display.IsCheckedChanged+=(_,_)=>{_app.Accounts.SetDisplay(name,display.IsChecked==true);Refresh(true);};AddCell(row,display,0);
                var update=new CheckBox{IsChecked=_app.Accounts.UpdateEnabled(name)};
                update.IsCheckedChanged+=(_,_)=>_app.Accounts.SetUpdate(name,update.IsChecked==true);AddCell(row,update,1);editable.Add(update);
                AddCell(row,Text(name,14),2);AddCell(row,Text(_app.IsDemo?"Demo":CredentialVault.Exists(name)?"Saved":"Manual",12,true),3);
                var last=_app.Store.Get<AccountProgress>("lastUpdate/"+name.ToLowerInvariant());
                var result=Text(last==null?"Not updated":last.State+" · "+last.Detail,11,true);resultLabels[name]=result;AddCell(row,result,4);
                var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=4};
                var edit=Button("Edit",async()=>{await EditCharacter(name,window);Render();});
                var remove=Button("Remove login",()=>
                {
                    _app.RequireLiveProfile();
                    if(_app.Busy)throw new InvalidOperationException("Stop the update before removing a login.");
                    CredentialVault.Remove(name);_app.Accounts.SetUpdate(name,false);_app.Accounts.SetDisplay(name,false);Render();Refresh(true);return Task.CompletedTask;
                });
                edit.Padding=new(9,6);remove.Padding=new(9,6);editable.Add(edit);editable.Add(remove);buttons.Children.Add(edit);buttons.Children.Add(remove);AddCell(row,buttons,5);
                table.Children.Add(new Border{Background=Brush("#191A1E"),CornerRadius=new(3),Child=row});
            }
        }
        var add=Button("Add character",async()=>{await EditCharacter(owner:window);Render();});
        var run=Button("Update selected accounts",async()=>
        {
            try{await _app.UpdateAccounts();message.Text=_app.QueueStatus;}
            catch(OperationCanceledException){message.Text="Update stopped.";}
            catch(Exception ex){message.Text=ex.Message;}
            Refresh(true);
        },"primary");
        var stop=Button("Stop",()=>{_app.Stop();message.Text="Stopping; safely logging out owned client…";return Task.CompletedTask;});
        actions.Children.Add(add);actions.Children.Add(run);actions.Children.Add(stop);panel.Children.Add(actions);
        panel.Children.Add(new ScrollViewer{Content=table,MaxHeight=430});panel.Children.Add(message);
        panel.Children.Add(Text("Passwords stay in Windows Credential Manager. Remove login clears that saved password and hides the character; saved item history is retained.",12,true));
        var done=new Button{Content="Close"};done.Click+=(_,_)=>window.Close();panel.Children.Add(done);Render();
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};
        timer.Tick+=(_,_)=>
        {
            add.IsEnabled=run.IsEnabled=!_app.Busy;stop.IsEnabled=_app.QueueRunning;
            foreach(var control in editable)control.IsEnabled=!_app.Busy;
            foreach(var (name,label) in resultLabels)if(_app.QueueProgress.TryGetValue(name,out var state))label.Text=state.State+(state.Detail.Length>0?" · "+state.Detail:"");
            if(_app.QueueRunning)message.Text=_app.QueueStatus;
        };
        timer.Start();window.Closed+=(_,_)=>timer.Stop();window.Content=panel;await window.ShowDialog(this);
    }
}
