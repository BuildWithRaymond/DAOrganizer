using DAOrganizer.Core;
using Xunit;
namespace DAOrganizer.Tests;

public class LoginFormTests
{
    [Fact] public async Task AllowsTextControlToFinishActivatingAfterDialogFocusChanges()
    {
        var form=new DelayedForm{Name="",DelayedEditorActivation=true,PasswordRequiresKeys=true};
        await LoginFormAutomation.Run(form,"Example","12345678",_=>{},CancellationToken.None);
        Assert.Equal("Example",form.Name);Assert.Equal("12345678",form.Ticket);Assert.Equal(1,form.Submissions);
    }
    [Fact] public async Task MaskedPasswordUsesKeyEventsAndReturnAdvancesFromName()
    {
        var form=new DelayedForm{PasswordRequiresKeys=true};
        await LoginFormAutomation.Run(form,"Example","12345678",_=>{},CancellationToken.None);
        Assert.Equal("12345678",form.Ticket);Assert.Equal(8,form.PasswordKeys);
        Assert.Equal(1,form.NameReturns);Assert.Equal(1,form.Submissions);
    }
    [Fact] public async Task WaitsForDelayedDialogFocusAndEachCharacterBeforeSubmitting()
    {
        var form=new DelayedForm();
        await LoginFormAutomation.Run(form,"Example","12345678",_=>{},CancellationToken.None);
        Assert.Equal("Example",form.Name);Assert.Equal("12345678",form.Ticket);Assert.Equal(1,form.Submissions);
    }
    [Fact] public async Task NoSubmissionWhenPasswordInputIsIgnored()
    {
        var form=new DelayedForm{IgnorePassword=true};
        var error=await Assert.ThrowsAsync<TimeoutException>(()=>LoginFormAutomation.Run(form,"Example","12345678",_=>{},CancellationToken.None));
        Assert.Contains("password",error.Message,StringComparison.OrdinalIgnoreCase);Assert.Equal(0,form.Submissions);
        Assert.DoesNotContain("12345678",error.Message);
    }
    [Fact] public async Task CancellationStopsBeforeAnyInput()
    {
        var form=new DelayedForm();using var cancel=new CancellationTokenSource();cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>LoginFormAutomation.Run(form,"Example","12345678",_=>{},cancel.Token));
        Assert.Equal(0,form.Inputs);Assert.Equal(0,form.Submissions);
    }
    private sealed class DelayedForm:ILoginForm
    {
        private int _tick,_due,_focus=2,_editableAt;private bool _visible;private Action? _pending;
        public string Name="Previous",Ticket="";
        public bool IgnorePassword;
        public bool PasswordRequiresKeys;
        public bool DelayedEditorActivation;
        public int PasswordKeys,NameReturns;
        public int Submissions,Inputs;
        public LoginFormState Read(string name,string ticket)=>new(_tick>=4,_visible,false,_focus,Name.Length,Ticket.Length,Name==name,Ticket==ticket);
        private void Queue(Action action){Inputs++;if(_pending!=null)return;_due=_tick+4;_pending=action;}
        public void Open(){if(_tick<4){Inputs++;return;}Queue(()=>{_visible=true;_editableAt=_tick+8;});}
        public void Tab()=>Queue(()=>_focus=_focus==2?3:2);
        public void End(){Inputs++;}
        public void Backspace()=>Queue(()=>{if(!_visible)return;if(_focus==2&&Name.Length>0)Name=Name[..^1];if(_focus==3&&Ticket.Length>0)Ticket=Ticket[..^1];});
        public void Type(char ch)=>Queue(()=>{if(!_visible||(DelayedEditorActivation&&_tick<_editableAt))return;if(_focus==2)Name+=ch;else if(!IgnorePassword&&!PasswordRequiresKeys)Ticket+=ch;});
        public void TypePassword(char ch){PasswordKeys++;Queue(()=>{if(_visible&&_focus==3&&!IgnorePassword&&(!DelayedEditorActivation||_tick>=_editableAt))Ticket+=ch;});}
        public void AcceptName(){NameReturns++;Queue(()=>{_focus=3;_editableAt=_tick+8;});}
        public void Submit(){Inputs++;Submissions++;}
        public Task Pause(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();_tick++;
            if(_pending!=null&&_tick>=_due){var action=_pending;_pending=null;action();}
            return Task.CompletedTask;
        }
    }
}
