using Avalonia.Controls;
using Avalonia.Layout;
using DAOrganizer.Core;

namespace DAOrganizer.App;

public sealed partial class MainWindow
{
    private async Task OrganizationPreview()
    {
        var window=DialogWindow("Organization plan",780);var panel=DialogPanel();
        var body=new StackPanel{Spacing=12};var status=Text("Review exact steps before approving a transfer.",12,true);
        var createdAt=DateTimeOffset.UtcNow;
        void Render()
        {
            body.Children.Clear();
            var state=_app.Store.ReadOrganizationState();
            var plan=OrganizationPlanner.Build(state);
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
            var exact=OrganizationPlanner.BuildExact(state,createdAt,createdAt.AddMinutes(30));
            body.Children.Add(Text("Exact step review",17));
            if(exact is null)body.Children.Add(Text("No exact steps from current saved scans and holder choices.",12,true));
            else
            {
                body.Children.Add(Text($"{exact.Steps.Length} candidates. Draft expires {exact.ExpiresAt.LocalDateTime:g}. Select steps to save a review draft.",12,true));
                var selected=new List<(Guid Id,CheckBox Box)>();
                foreach(var step in exact.Steps)
                {
                    var row=new CheckBox{Content=$"{step.SourceCharacter} {step.SourceLocation} slot {step.SourceSlot}: {step.Quantity} {step.SourceItem.Name} → {step.DestinationCharacter} Bank | {step.RouteKind} | {step.Readiness}",
                        IsChecked=step.Readiness!=OrganizationReadiness.ManualOnly};
                    selected.Add((step.Id,row));body.Children.Add(row);
                }
                if(!_app.IsDemo)
                    body.Children.Add(Button("Save selected review draft",()=>
                    {
                        try
                        {
                            var ids=selected.Where(x=>x.Box.IsChecked==true).Select(x=>x.Id).ToArray();
                            var chosen=OrganizationPlanSelection.Select(exact,state,ids);
                            if(DateTimeOffset.UtcNow>=chosen.ExpiresAt)
                                throw new InvalidOperationException("Review draft expired. Reopen organization review.");
                            if(_app.Store.ReadOrganizationState().Fingerprint!=state.Fingerprint)
                                throw new StaleOrganizationPlanException("Saved state changed. Reopen organization review.");
                            if(_app.Store.LoadOrganizationPlan(chosen.Id) is null)
                                _app.Store.SaveOrganizationPlan(chosen);
                            status.Text=$"Review draft {chosen.Id:D} has {chosen.Steps.Length} selected steps.";
                            Render();
                        }
                        catch(Exception ex){status.Text=ex.Message;}
                        return Task.CompletedTask;
                    }));
                if(!_app.IsDemo)
                {
                    Task Prepare(bool trial)
                    {
                        try
                        {
                            var ids=selected.Where(x=>x.Box.IsChecked==true).Select(x=>x.Id).ToArray();
                            var chosen=OrganizationPlanSelection.Select(exact,state,ids);
                            if(chosen.Steps.Length!=1)throw new InvalidOperationException("Select exactly one direct step.");
                            var step=chosen.Steps[0];
                            var sender=_app.Session(step.SourceCharacter)??throw new InvalidOperationException("Source is offline.");
                            var recipient=_app.Session(step.DestinationCharacter)??throw new InvalidOperationException("Recipient is offline.");
                            var now=DateTimeOffset.UtcNow;
                            var ready=DirectPlanReadiness.Promote(chosen,state,
                                sender.CaptureTradeEndpoint(recipient.Name,now),
                                recipient.CaptureTradeEndpoint(sender.Name,now),now,trial);
                            if(_app.Store.ReadOrganizationState().Fingerprint!=state.Fingerprint)
                                throw new StaleOrganizationPlanException("Saved state changed. Reopen organization review.");
                            ready=ready with{Id=Guid.NewGuid()};
                            _app.Store.SaveOrganizationPlan(ready);
                            status.Text=trial?
                                "One-unit controlled trial saved. It may stop with the item on either character. Review, then Approve.":
                                "Ready draft saved. Review the exact step below, then Approve.";
                            Render();
                        }
                        catch(Exception ex){status.Text=ex.Message;}
                        return Task.CompletedTask;
                    }
                    body.Children.Add(Button("Prepare selected direct step",()=>Prepare(false)));
                    body.Children.Add(Button("Prepare one-unit controlled trial",()=>Prepare(true)));
                }
                body.Children.Add(Text("For a ready direct step: both clients must be adjacent and mutually visible; an inventory source is exact, or a bank source can withdraw one uniquely named unit; destination bank has a scanned matching stack with known room; trade evidence is positive. Select one step, prepare, then approve.",11,true));
                body.Children.Add(Text("Controlled trial permits one low-value unit before tradeability or bank capacity is known. If exchange or deposit is refused, recovery shows the last verified holder. No automatic retry is sent.",11,true));
            }
            if(!_app.IsDemo)
            {
                var saved=_app.Store.ListOrganizationPlans(10);
                if(saved.Count>0)body.Children.Add(Text("Saved review drafts",17));
                foreach(var entry in saved)
                {
                    var draft=entry;
                    var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
                    row.Children.Add(Text($"{draft.Plan.CreatedAt.LocalDateTime:g} | {draft.Plan.Steps.Length} steps | {draft.Approval}"+
                        (draft.Plan.Steps.Any(x=>x.ControlledTrial)?" | CONTROLLED TRIAL":"")+
                        $" | next {draft.NextStepOrdinal+1} | expires {draft.Plan.ExpiresAt.LocalDateTime:g}",11,true));
                    var approve=Button("Approve",()=>
                    {
                        try{_app.Store.ApproveOrganizationPlan(draft.Plan.Id,DateTimeOffset.UtcNow);status.Text="Plan approved. Run the direct transfer when both clients are ready.";Render();}
                        catch(Exception ex){status.Text=ex.Message;}
                        return Task.CompletedTask;
                    });
                    approve.IsEnabled=draft.Approval==PlanApprovalState.Draft&&draft.Plan.ExpiresAt>DateTimeOffset.UtcNow&&
                        draft.Plan.Steps.All(x=>x.Readiness==OrganizationReadiness.Ready);
                    row.Children.Add(approve);
                    if(draft.Approval==PlanApprovalState.Approved&&draft.Plan.Steps.Length==1&&
                        draft.Plan.Steps[0].RouteKind==TransferRouteKind.Direct)
                    {
                        var run=Button("Run direct transfer",async()=>
                        {
                            try
                            {
                                status.Text="Transfer running. Keep both clients open and do not move items manually.";
                                await _app.RunOperation(t=>_app.ExecuteApprovedDirectPlan(draft.Plan.Id,t));
                                status.Text="Transfer and destination bank confirmed.";
                            }
                            catch(Exception ex){status.Text="Transfer stopped: "+ex.Message+" Review recovery below.";}
                            Render();
                        });
                        run.IsEnabled=!_app.Busy;row.Children.Add(run);
                    }
                    body.Children.Add(row);
                }
                var runs=_app.Store.ListTransferRuns(10);
                if(runs.Count>0)body.Children.Add(Text("Transfer recovery",17));
                foreach(var run in runs)
                    body.Children.Add(Text($"{run.State}: {run.Quantity} from {run.SourceCharacter} to {run.DestinationCharacter}. Last verified holder: {run.LastVerifiedHolder}. {run.Reason}",12,true));
            }
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
