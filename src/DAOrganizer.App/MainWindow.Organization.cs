using Avalonia.Controls;
using Avalonia.Layout;
using DAOrganizer.Core;

namespace DAOrganizer.App;

public sealed partial class MainWindow
{
    private async Task OrganizationPreview()
    {
        var window=DialogWindow("Organization plan",780);var panel=DialogPanel();
        var body=new StackPanel{Spacing=12};var status=Text("Analysis only. No items will move.",12,true);
        void Render()
        {
            body.Children.Clear();
            var plan=OrganizationPlanner.Build(_app.Store.ReadOrganizationState());
            body.Children.Add(Text("Cross-character bank opportunities",22));
            if(!_app.IsDemo)
            {
                body.Children.Add(Text("Manual trade packet capture",17));
                if(_app.ManualTradeCaptureRunning)
                {
                    body.Children.Add(Button("Stop and save capture",()=>
                    {
                        try
                        {
                            var path=_app.StopManualTradeCapture();
                            status.Text="Saved locally: "+path+(_app.LastManualTradeAnalysis?.Verified==true?
                                " | Item transfer verified; local tradeability evidence saved.":
                                " | Transfer unverified; no tradeability evidence saved. "+_app.LastManualTradeAnalysis?.Reason);
                            Render();
                        }
                        catch(Exception ex){status.Text=ex.Message;}
                        return Task.CompletedTask;
                    }));
                }
                else
                {
                    var online=_app.Sessions.Where(x=>x.Ready).Select(x=>x.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    var controls=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
                    var first=new ComboBox{ItemsSource=online,SelectedIndex=online.Length>0?0:-1,Width=145};
                    var second=new ComboBox{ItemsSource=online,SelectedIndex=online.Length>1?1:-1,Width=145};
                    controls.Children.Add(first);controls.Children.Add(second);
                    var start=Button("Start capture",()=>
                    {
                        try
                        {
                            var id=_app.StartManualTradeCapture((string?)first.SelectedItem??"",(string?)second.SelectedItem??"");
                            status.Text="Capture "+id+" running. Make one small manual trade in the game, then stop and save.";Render();
                        }
                        catch(Exception ex){status.Text=ex.Message;}
                        return Task.CompletedTask;
                    });
                    start.IsEnabled=online.Length>=2;controls.Children.Add(start);
                    body.Children.Add(controls);
                    if(online.Length<2)body.Children.Add(Text("Connect two distinct ready clients to enable capture.",11,true));
                }
                body.Children.Add(Text("Use two ready clients. Start capture, manually trade a small item and stack amount, finish both accepts, then save. Local JSON contains character names, item names, and packet payloads; redact names before sharing. No packets are sent by this feature.",11,true));
            }
            body.Children.Add(Text($"{plan.Groups.Count} duplicate groups · {plan.CurrentBankSlots} current bank entries · up to {plan.PotentialSlotsFreed} entries could be freed",14));
            body.Children.Add(Text("Potential savings assume stacks can fit. Confirmed savings stay zero until stack limits, bank capacity, tradeability, and exchange are verified.",12,true));
            if(plan.Groups.Count==0)body.Children.Add(Text("No duplicate bank groups found in saved scans.",13,true));
            foreach(var group in plan.Groups)
            {
                var card=new StackPanel{Spacing=7};
                card.Children.Add(Text(group.Item.Name,17));
                card.Children.Add(Text($"{group.Owners.Count} characters · {group.CurrentBankSlots} bank entries → {group.PotentialBankSlots} potential · up to {group.PotentialSlotsFreed} freed",13));
                card.Children.Add(Text("Owners: "+string.Join(", ",group.Owners),12,true));
                card.Children.Add(Text("Suggested holder: "+(group.ProposedHolder??"Choose to resolve conflict"),12,true));
                var holderRow=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
                holderRow.Children.Add(Text("Preferred holder",12,true));
                var choices=new[]{"Use suggestion"}.Concat(_app.Store.Characters().Select(x=>x.Name)).ToArray();
                var old=_app.Store.GetItemOverride(group.Item);
                var holder=new ComboBox{ItemsSource=choices,SelectedItem=old?.DestinationCharacter??"Use suggestion",Width=175};
                holderRow.Children.Add(holder);
                holderRow.Children.Add(Button("Save choice",()=>
                {
                    if(holder.SelectedItem is string chosen)
                    {
                        var preference=_app.Store.GetItemOverride(group.Item);
                        var destination=chosen=="Use suggestion"?null:chosen;
                        _app.Store.SetItemOverride(group.Item,new ItemOverride(preference?.Category,destination,preference?.NeverMove??false));
                        status.Text=destination==null?$"Cleared preferred holder for {group.Item.Name}.":$"Saved {destination} as preferred holder for {group.Item.Name}.";
                        Render();
                    }
                    return Task.CompletedTask;
                }));card.Children.Add(holderRow);
                foreach(var route in group.Routes)card.Children.Add(Text($"{route.Source} ({route.SourceLocation}) → {route.Destination}: {route.Kind}"+
                    (route.Middleman is { } middle?$" via {middle}":"")+" · "+route.Reason,12,true));
                if(group.Blockers.Count>0)card.Children.Add(Text("Before transfer: "+string.Join("; ",group.Blockers.Distinct()),11,true));
                body.Children.Add(new Border{Padding=new(12),Background=Brush("#191A1E"),Child=card});
            }
        }
        Render();panel.Children.Add(new ScrollViewer{Content=body,MaxHeight=630});panel.Children.Add(status);
        var close=new Button{Content="Close"};close.Click+=(_,_)=>window.Close();panel.Children.Add(close);
        window.Content=panel;await window.ShowDialog(this);
    }
}
