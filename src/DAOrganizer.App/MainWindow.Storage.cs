using Avalonia.Controls;
using Avalonia.Layout;
using DAOrganizer.Core;

namespace DAOrganizer.App;

public sealed partial class MainWindow
{
    private async Task StorageSetup(Window owner)
    {
        var window=DialogWindow("Game accounts & storage",670);
        var panel=DialogPanel();var body=new StackPanel{Spacing=16};
        var message=Text("Account links and storage preferences stay on this computer.",12,true);
        var selectedCharacter=_app.Store.Characters().FirstOrDefault()?.Name;
        var selectedFirst=0L;var selectedSecond=0L;
        void Try(Action action)
        {
            try{action();message.Text="Saved.";Render();}
            catch(Exception ex){message.Text=ex.Message;}
        }
        void Render()
        {
            body.Children.Clear();
            var accounts=_app.Accounts.GameAccounts();
            body.Children.Add(Text("Game accounts",19));
            var createRow=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
            var label=new TextBox{Watermark="Account label",Width=220};createRow.Children.Add(label);
            createRow.Children.Add(Button("Add account",()=>{Try(()=>_app.Accounts.CreateGameAccount(label.Text??""));return Task.CompletedTask;}));
            body.Children.Add(createRow);
            if(accounts.Count>0)
            {
                var pair=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
                var first=new ComboBox{ItemsSource=accounts,SelectedItem=accounts.FirstOrDefault(x=>x.Id==selectedFirst)??accounts[0],Width=165};
                var second=new ComboBox{ItemsSource=accounts,SelectedItem=accounts.FirstOrDefault(x=>x.Id==selectedSecond)??accounts[^1],Width=165};
                var policy=new ComboBox{ItemsSource=Enum.GetValues<CoexistencePolicy>(),SelectedItem=CoexistencePolicy.Unknown,Width=115};
                void RefreshPair()
                {
                    selectedFirst=(first.SelectedItem as GameAccount)?.Id??0;
                    selectedSecond=(second.SelectedItem as GameAccount)?.Id??0;
                    policy.SelectedItem=selectedFirst>0&&selectedSecond>0&&selectedFirst!=selectedSecond?
                        _app.Accounts.PairCoexistence(selectedFirst,selectedSecond):CoexistencePolicy.Unknown;
                }
                first.SelectionChanged+=(_,_)=>RefreshPair();
                second.SelectionChanged+=(_,_)=>RefreshPair();
                RefreshPair();
                pair.Children.Add(first);pair.Children.Add(second);pair.Children.Add(policy);
                pair.Children.Add(Button("Set pair",()=>
                {
                    Try(()=>_app.Accounts.SetPairCoexistence(((GameAccount)first.SelectedItem!).Id,((GameAccount)second.SelectedItem!).Id,(CoexistencePolicy)policy.SelectedItem!));
                    return Task.CompletedTask;
                }));body.Children.Add(pair);
                body.Children.Add(Text("Set pair to Yes only when those accounts can be online together. Unknown stays manual-only.",12,true));
                foreach(var account in accounts)
                {
                    var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
                    row.Children.Add(Text(account.Label,14));row.Children.Add(Text("Same account online:",12,true));
                    var coexist=new ComboBox{ItemsSource=Enum.GetValues<CoexistencePolicy>(),SelectedItem=account.SameAccountCoexistence,Width=115};
                    coexist.SelectionChanged+=(_,_)=>Try(()=>_app.Accounts.SetSameAccountCoexistence(account.Id,(CoexistencePolicy)coexist.SelectedItem!));
                    row.Children.Add(coexist);body.Children.Add(row);
                }
            }
            body.Children.Add(Text("Character",19));
            var characters=_app.Store.Characters().Select(x=>x.Name).ToArray();
            if(characters.Length==0){body.Children.Add(Text("Add a character first.",12,true));return;}
            if(!characters.Contains(selectedCharacter,StringComparer.OrdinalIgnoreCase))selectedCharacter=characters[0];
            var characterPicker=new ComboBox{ItemsSource=characters,SelectedItem=selectedCharacter,Width=220};
            characterPicker.SelectionChanged+=(_,_)=>{selectedCharacter=characterPicker.SelectedItem as string;Render();};
            body.Children.Add(characterPicker);
            var character=selectedCharacter!;
            var assignment=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
            assignment.Children.Add(Text("Game account",12,true));
            var accountChoices=new[]{"Unassigned"}.Concat(accounts.Select(x=>x.Label)).ToArray();
            var assigned=_app.Accounts.AccountFor(character);
            var accountPicker=new ComboBox{ItemsSource=accountChoices,SelectedItem=assigned?.Label??"Unassigned",Width=190};
            accountPicker.SelectionChanged+=(_,_)=>Try(()=>
            {
                var chosen=accounts.FirstOrDefault(x=>x.Label==(string?)accountPicker.SelectedItem);
                if(chosen==null)_app.Accounts.UnassignCharacter(character);
                else _app.Accounts.AssignCharacter(character,chosen.Id);
            });
            assignment.Children.Add(accountPicker);body.Children.Add(assignment);
            var middleman=new CheckBox{Content="Trusted middleman candidate",IsChecked=_app.Accounts.MiddlemanCapable(character)};
            middleman.IsCheckedChanged+=(_,_)=>Try(()=>_app.Accounts.SetMiddlemanCapable(character,middleman.IsChecked==true));
            body.Children.Add(middleman);
            body.Children.Add(Text("Storage roles",19));
            var roleRow=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
            var roleLabel=new TextBox{Watermark="Role name (for example, Chests)",Width=275};
            roleRow.Children.Add(roleLabel);
            roleRow.Children.Add(Button("Add role",()=>{Try(()=>_app.Accounts.AddStorageRole(character,roleLabel.Text??""));return Task.CompletedTask;}));
            body.Children.Add(roleRow);
            foreach(var role in _app.Accounts.StorageRoles(character))
            {
                var card=new StackPanel{Spacing=7};
                var heading=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
                var enabled=new CheckBox{Content=role.Label,IsChecked=role.Enabled};
                enabled.IsCheckedChanged+=(_,_)=>Try(()=>_app.Accounts.SetStorageRoleEnabled(role.Id,enabled.IsChecked==true));
                heading.Children.Add(enabled);
                heading.Children.Add(Button("Remove role",()=>{Try(()=>_app.Accounts.RemoveStorageRole(role.Id));return Task.CompletedTask;}));
                card.Children.Add(heading);
                foreach(var rule in _app.Accounts.StorageRules(role.Id))
                {
                    var ruleRow=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
                    ruleRow.Children.Add(Text(rule.MatchKind==StorageMatchKind.All?"All items":$"{rule.MatchKind}: {rule.MatchValue}",12,true));
                    ruleRow.Children.Add(Button("Remove",()=>{Try(()=>_app.Accounts.RemoveStorageRule(rule.Id));return Task.CompletedTask;}));
                    card.Children.Add(ruleRow);
                }
                var addRule=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
                var kind=new ComboBox{ItemsSource=Enum.GetValues<StorageMatchKind>(),SelectedItem=StorageMatchKind.Category,Width=105};
                var value=new TextBox{Watermark="Category or sprite:color:name",Width=285};
                var categoryChoices=ItemCategories.Names.Concat(_app.Accounts.Categorize(_app.Store.Search("").Select(x=>x.Item))
                    .Select(ItemCategories.Family).Where(x=>x!="General")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var categories=new ComboBox{ItemsSource=categoryChoices,PlaceholderText="Choose category or family",Width=225};
                categories.SelectionChanged+=(_,_)=>{if(kind.SelectedItem is StorageMatchKind.Category&&categories.SelectedItem is string chosen)value.Text=chosen;};
                var itemChoices=_app.Store.Search("").Select(x=>x.Item).DistinctBy(ItemGroups.Key)
                    .OrderBy(x=>x.Name,StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x=>$"{x.Name} [{x.Sprite}:{x.Color}]",ItemGroups.Key);
                var observedItems=new ComboBox{ItemsSource=itemChoices.Keys.ToArray(),PlaceholderText="Choose observed item",Width=280};
                observedItems.SelectionChanged+=(_,_)=>{if(kind.SelectedItem is StorageMatchKind.Item&&observedItems.SelectedItem is string chosen)value.Text=itemChoices[chosen];};
                kind.SelectionChanged+=(_,_)=>
                {
                    var selected=(StorageMatchKind)kind.SelectedItem!;
                    value.IsEnabled=selected!=StorageMatchKind.All;
                    categories.IsVisible=selected==StorageMatchKind.Category;
                    observedItems.IsVisible=selected==StorageMatchKind.Item;
                    value.Text="";
                };
                addRule.Children.Add(kind);addRule.Children.Add(value);
                addRule.Children.Add(Button("Add rule",()=>
                {
                    Try(()=>_app.Accounts.AddStorageRule(role.Id,(StorageMatchKind)kind.SelectedItem!,
                        (StorageMatchKind)kind.SelectedItem! ==StorageMatchKind.All?"":value.Text??""));
                    return Task.CompletedTask;
                }));
                card.Children.Add(addRule);
                card.Children.Add(categories);card.Children.Add(observedItems);observedItems.IsVisible=false;
                body.Children.Add(new Border{Padding=new(10),Background=Brush("#191A1E"),Child=card});
            }
            body.Children.Add(Text("Item rules use sprite:color:NAME. Open an item detail to set its exact preferred holder. Storage roles only recommend destinations; they do not move items.",12,true));
        }
        Render();panel.Children.Add(new ScrollViewer{Content=body,MaxHeight=620});panel.Children.Add(message);
        var close=new Button{Content="Close"};close.Click+=(_,_)=>window.Close();panel.Children.Add(close);
        window.Content=panel;await window.ShowDialog(owner);
    }
}
