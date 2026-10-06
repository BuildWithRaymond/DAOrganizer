using Avalonia.Controls;
using Avalonia.Layout;
using DAOrganizer.Core;
using DAOrganizer.Game;

namespace DAOrganizer.App;

public sealed partial class MainWindow
{
    private async Task OrganizationPreview()
    {
        var window=DialogWindow("Organization plan",780);var panel=DialogPanel();
        var body=new StackPanel{Spacing=12};var status=Text("Choose a Direct route, then confirm the one-unit transfer.",12,true);
        var createdAt=DateTimeOffset.UtcNow;
        string? selectedRoute=null;
        void Render()
        {
            body.Children.Clear();
            var state=_app.Store.ReadOrganizationState();
            var plan=OrganizationPlanner.Build(state);
            body.Children.Add(Text("Cross-character bank opportunities",22));
            if(!_app.IsDemo)
            {
                var captureSection=new StackPanel{Spacing=8};
                captureSection.Children.Add(Text("Manual trade packet capture",17));
                if(_app.ManualTradeCaptureRunning)
                {
                    captureSection.Children.Add(Button("Stop and save capture",()=>
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
                    captureSection.Children.Add(controls);
                    if(online.Length<2)captureSection.Children.Add(Text("Connect two distinct ready clients to enable capture.",11,true));
                }
                captureSection.Children.Add(Text("Use two ready clients. Start capture, manually trade a small item and stack amount, finish both accepts, then save. Local JSON contains character names, item names, and packet payloads; redact names before sharing. No packets are sent by this feature.",11,true));
                body.Children.Add(new Expander{Header="Manual trade capture (advanced)",Content=captureSection});
            }
            body.Children.Add(Text($"{plan.Groups.Count} duplicate groups · {plan.CurrentBankSlots} current bank entries · up to {plan.PotentialSlotsFreed} entries could be freed",14));
            body.Children.Add(Text("Potential savings assume stacks can fit. Confirmed savings stay zero until stack limits, bank capacity, tradeability, and exchange are verified.",12,true));
            var exact=OrganizationPlanner.BuildExact(state,createdAt,createdAt.AddMinutes(30));
            body.Children.Add(Text("Transfer between characters",17));
            if(exact is null)body.Children.Add(Text("No exact steps from current saved scans and holder choices.",12,true));
            else
            {
                body.Children.Add(Text("Choose a Direct route to move one unit. Saved review drafts are under Advanced review.",12,true));
                var selected=new List<(Guid Id,CheckBox Box)>();
                var candidateRows=new StackPanel{Spacing=10};
                foreach(var step in exact.Steps)
                {
                    var row=new CheckBox{Content=$"{step.SourceCharacter} {step.SourceLocation} slot {step.SourceSlot}: {step.Quantity} {step.SourceItem.Name} → {step.DestinationCharacter} Bank | {step.RouteKind} | {step.Readiness}",
                        IsChecked=_app.IsDemo&&step.Readiness!=OrganizationReadiness.ManualOnly};
                    selected.Add((step.Id,row));candidateRows.Children.Add(row);
                }
                if(!_app.IsDemo)
                {
                    var direct=exact.Steps.Where(x=>x.RouteKind==TransferRouteKind.Direct).ToArray();
                    body.Children.Add(Text($"{direct.Length} Direct routes",17));
                    if(direct.Length==0)
                    {
                        body.Children.Add(Text("No Direct route yet. Assign the two characters to separate game accounts and set their coexistence to Yes.",12,true));
                        body.Children.Add(Button("Configure account coexistence",async()=>
                        {
                            await StorageSetup(window);
                            Render();
                        }));
                    }
                    else
                    {
                        body.Children.Add(Text("Choose one route. Transfer moves one unit after you confirm the exact item and characters.",12,true));
                        var labels=direct.Select(x=>$"{x.SourceCharacter} {x.SourceLocation} slot {x.SourceSlot}: {x.SourceItem.Name} → {x.DestinationCharacter} Bank").ToArray();
                        var choice=new ComboBox{ItemsSource=labels,SelectedIndex=Array.IndexOf(labels,selectedRoute),Width=680,PlaceholderText="Choose one Direct route"};
                        choice.SelectionChanged+=(_,_)=>selectedRoute=choice.SelectedItem as string;
                        body.Children.Add(choice);
                        body.Children.Add(Button("Transfer one unit",async() =>
                        {
                            try
                            {
                                if(choice.SelectedIndex<0||choice.SelectedIndex>=direct.Length)
                                    throw new InvalidOperationException("Choose one Direct route from the list above.");
                                var step=direct[choice.SelectedIndex];
                                if(!await ConfirmOneUnitTransfer(window,step))return;
                                status.Text="Refreshing banks and running the one-unit transfer. Keep both clients open.";
                                await _app.RunOperation(t=>_app.ExecuteOneUnitDirectTrial(step,t));
                                status.Text="Transfer and destination bank confirmed.";
                            }
                            catch(Exception ex){status.Text="Transfer stopped: "+ex.Message+" Review recovery below.";}
                            Render();
                        },"primary"));
                        body.Children.Add(new Expander{Header="Visibility details (advanced)",Content=Button("Check live visibility",() =>
                        {
                            try
                            {
                                if(choice.SelectedIndex<0||choice.SelectedIndex>=direct.Length)
                                    throw new InvalidOperationException("Choose one Direct route from the list above.");
                                var step=direct[choice.SelectedIndex];
                                var sender=_app.Session(step.SourceCharacter)??throw new InvalidOperationException("Source is offline.");
                                var recipient=_app.Session(step.DestinationCharacter)??throw new InvalidOperationException("Recipient is offline.");
                                var now=DateTimeOffset.UtcNow;
                                string Line(TradeTargetInspection view)
                                {
                                    var age=view.LatestMatchAt is { } seen?$"{Math.Max(0,(int)(now-seen).TotalSeconds)}s ago":"never";
                                    return $"{view.Name}: {(view.Connected?"connected":"disconnected")}, map {view.MapId} ({view.Position.X},{view.Position.Y}), own ID {(view.PlayerIdKnown?"yes":"no")}, partner {view.NamedMatches} named/{view.RecentMatches} visible (last {age}), other visible {view.VisibleTargets}; draw packets 0x07={view.EntityDrawPackets}, 0x33={view.HumanDrawPackets}.";
                                }
                                status.Text=Line(sender.InspectTradeTarget(recipient.Name,now))+"\n"+
                                    Line(recipient.InspectTradeTarget(sender.Name,now));
                            }
                            catch(Exception ex){status.Text=ex.Message;}
                            return Task.CompletedTask;
                        })});
                    }
                }
                var review=new StackPanel{Spacing=10};
                review.Children.Add(Text("Review candidates",17));
                if(!_app.IsDemo)
                    review.Children.Add(Button("Save selected review draft",()=>
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
                review.Children.Add(new ScrollViewer{Content=candidateRows,MaxHeight=220});
                review.Children.Add(Text("Controlled trial permits one low-value unit before tradeability or bank capacity is known. If exchange or deposit is refused, recovery shows the last verified holder. No automatic retry is sent.",11,true));
                body.Children.Add(new Expander{Header="Advanced review drafts",Content=review});
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
                {
                    body.Children.Add(Text($"{run.State}: {run.Quantity} from {run.SourceCharacter} to {run.DestinationCharacter}. Last verified holder: {run.LastVerifiedHolder}. {run.Reason}",12,true));
                    if(run.State==TransferRunState.NeedsReconciliation&&run.Quantity==1&&
                       run.Reason=="Approach and scan the same nearby banker before an approved deposit."&&
                       run.LastVerifiedHolder.Equals(run.DestinationCharacter,StringComparison.OrdinalIgnoreCase))
                    {
                        var pending=run;
                        var step=_app.Store.LoadOrganizationPlan(run.PlanId)?.Plan.Steps.SingleOrDefault(x=>x.Id==run.StepId);
                        var finish=Button($"Deposit {step?.SourceItem.Name??"delivered unit"} on {run.DestinationCharacter}",async() =>
                        {
                            try
                            {
                                status.Text="Checking delivered unit and destination bank. Keep recipient client open.";
                                await _app.RunOperation(t=>_app.FinishDeliveredTransferBanking(pending.Id,t));
                                status.Text="Destination bank deposit confirmed.";
                            }
                            catch(Exception ex){status.Text="Deposit stopped: "+ex.Message+" Review recovery below.";}
                            Render();
                        });
                        finish.IsEnabled=!_app.Busy&&_app.Session(run.DestinationCharacter)?.Ready==true;
                        body.Children.Add(finish);
                    }
                }
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

    private async Task<bool> ConfirmOneUnitTransfer(Window owner,PlannedOrganizationStep step)
    {
        var window=DialogWindow("Confirm one-unit transfer",480);var panel=DialogPanel();
        panel.Children.Add(Text($"Move 1 {step.SourceItem.Name}",20));
        panel.Children.Add(Text($"From: {step.SourceCharacter} {step.SourceLocation} slot {step.SourceSlot}\nTo: {step.DestinationCharacter} Bank",14));
        panel.Children.Add(Text("Both clients must stay open. The organizer will refresh bank contents, exchange one unit, and bank it. If confirmation fails, check Transfer recovery before trying again.",12,true));
        var confirmed=false;
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        buttons.Children.Add(Button("Confirm transfer",() =>{confirmed=true;window.Close();return Task.CompletedTask;},"primary"));
        buttons.Children.Add(Button("Cancel",() =>{window.Close();return Task.CompletedTask;}));
        panel.Children.Add(buttons);window.Content=panel;
        await window.ShowDialog(owner);
        return confirmed;
    }
}
