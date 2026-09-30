using System.Collections.Immutable;
using System.Text.Json;

namespace DAOrganizer.Core;

public sealed partial class InventoryStore
{
    public void SaveOrganizationPlan(ExactOrganizationPlan plan)
    {
        OrganizationPlanContract.Validate(plan);
        lock(_gate)
        {
            using var transaction=_db.BeginTransaction();
            Execute("INSERT INTO organization_plans(id,created_at,expires_at,input_fingerprint,approval) VALUES($id,$created,$expires,$fingerprint,'Draft')",
                ("$id",plan.Id.ToString("D")),("$created",plan.CreatedAt.ToString("O")),
                ("$expires",plan.ExpiresAt.ToString("O")),("$fingerprint",plan.InputFingerprint));
            foreach(var step in plan.Steps)
                Execute("INSERT INTO plan_steps(plan_id,ordinal,step_id,data) VALUES($plan,$order,$id,$data)",
                    ("$plan",plan.Id.ToString("D")),("$order",step.Order),("$id",step.Id.ToString("D")),
                    ("$data",JsonSerializer.Serialize(step)));
            transaction.Commit();
        }
    }

    public SavedOrganizationPlan? LoadOrganizationPlan(Guid id)
    {
        lock(_gate)return LoadOrganizationPlanCore(id);
    }

    private SavedOrganizationPlan? LoadOrganizationPlanCore(Guid id)
    {
        using var command=_db.CreateCommand();
        command.CommandText="SELECT created_at,expires_at,input_fingerprint,approval,checkpoint_fingerprint,next_step_ordinal FROM organization_plans WHERE id=$id";
        command.Parameters.AddWithValue("$id",id.ToString("D"));
        using var reader=command.ExecuteReader();
        if(!reader.Read())return null;
        DateTimeOffset created,expires;
        try
        {
            created=DateTimeOffset.Parse(reader.GetString(0));expires=DateTimeOffset.Parse(reader.GetString(1));
        }
        catch(FormatException e){throw new InvalidDataException("Saved plan dates are malformed.",e);}
        var fingerprint=reader.GetString(2);var approvalText=reader.GetString(3);
        if(!Enum.TryParse<PlanApprovalState>(approvalText,false,out var approval)||!Enum.IsDefined(approval))
            throw new InvalidDataException("Saved plan approval is malformed.");
        var checkpoint=reader.IsDBNull(4)?null:reader.GetString(4);
        var next=reader.GetInt32(5);
        reader.Close();
        using var stepsCommand=_db.CreateCommand();
        stepsCommand.CommandText="SELECT ordinal,step_id,data FROM plan_steps WHERE plan_id=$id ORDER BY ordinal";
        stepsCommand.Parameters.AddWithValue("$id",id.ToString("D"));
        using var stepsReader=stepsCommand.ExecuteReader();var steps=ImmutableArray.CreateBuilder<PlannedOrganizationStep>();
        while(stepsReader.Read())
        {
            PlannedOrganizationStep? step;
            try{step=JsonSerializer.Deserialize<PlannedOrganizationStep>(stepsReader.GetString(2));}
            catch(JsonException e){throw new InvalidDataException("Saved plan step JSON is malformed.",e);}
            if(step is null||step.Order!=stepsReader.GetInt32(0)||step.Id.ToString("D")!=stepsReader.GetString(1))
                throw new InvalidDataException("Saved plan step identity is malformed.");
            steps.Add(step);
        }
        var plan=new ExactOrganizationPlan(id,created,expires,fingerprint,steps.ToImmutable());
        OrganizationPlanContract.Validate(plan);
        if(next>plan.Steps.Length||approval==PlanApprovalState.Draft&&(next!=0||checkpoint!=null)||
            approval==PlanApprovalState.Approved&&(next>=plan.Steps.Length||checkpoint==null)||
            approval==PlanApprovalState.Completed&&(next!=plan.Steps.Length||checkpoint==null))
            throw new InvalidDataException("Saved plan checkpoint is malformed.");
        return new(plan,approval,checkpoint,next);
    }

    public SavedOrganizationPlan ApproveOrganizationPlan(Guid id,DateTimeOffset now)
    {
        lock(_gate)
        {
            Execute("BEGIN IMMEDIATE TRANSACTION");
            try
            {
                var saved=LoadOrganizationPlanCore(id)??throw new KeyNotFoundException("Organization plan not found.");
                if(saved.Approval!=PlanApprovalState.Draft)throw new InvalidOperationException("Plan is already approved.");
                if(now>=saved.Plan.ExpiresAt)throw new InvalidOperationException("Plan has expired.");
                if(saved.Plan.Steps.Any(x=>x.Readiness!=OrganizationReadiness.Ready))
                    throw new InvalidOperationException("Plan contains steps that are not ready.");
                var state=ReadOrganizationStateWithinTransaction();
                if(state.Fingerprint!=saved.Plan.InputFingerprint||
                    !OrganizationPlanContract.Matches(state,saved.Plan.Steps[0].ExpectedBefore)||
                    !OrganizationPlanContract.MatchesSource(state,saved.Plan.Steps[0]))
                    throw new StaleOrganizationPlanException("Plan inputs changed. Replan before approval.");
                Execute("UPDATE organization_plans SET approval='Approved',checkpoint_fingerprint=$fingerprint,checkpoint_state=$state WHERE id=$id",
                    ("$fingerprint",state.Fingerprint),("$state",JsonSerializer.Serialize(state)),("$id",id.ToString("D")));
                Execute("COMMIT");
                return LoadOrganizationPlanCore(id)!;
            }
            catch{Execute("ROLLBACK");throw;}
        }
    }

    public PlannedOrganizationStep? CheckNextOrganizationStep(Guid id,DateTimeOffset now)
    {
        lock(_gate)
        {
            var saved=LoadOrganizationPlanCore(id)??throw new KeyNotFoundException("Organization plan not found.");
            if(saved.Approval==PlanApprovalState.Completed)return null;
            if(saved.Approval!=PlanApprovalState.Approved)throw new InvalidOperationException("Plan is not approved.");
            if(now>=saved.Plan.ExpiresAt)throw new InvalidOperationException("Plan has expired.");
            var state=ReadOrganizationState();
            var step=saved.Plan.Steps[saved.NextStepOrdinal];
            if(state.Fingerprint!=saved.CheckpointFingerprint||!OrganizationPlanContract.Matches(state,step.ExpectedBefore)||
                !OrganizationPlanContract.MatchesSource(state,step))
                throw new StaleOrganizationPlanException("Approved plan checkpoint changed. Replan or reconcile.");
            return step;
        }
    }

    // Phase 4 executor must call only after independent game/bank evidence verifies the step.
    // This records a local checkpoint; it does not itself prove delivery or send packets.
    public SavedOrganizationPlan ConfirmOrganizationStep(Guid id,Guid stepId,string beforeFingerprint,DateTimeOffset now)
    {
        lock(_gate)
        {
            Execute("BEGIN IMMEDIATE TRANSACTION");
            try
            {
                var saved=LoadOrganizationPlanCore(id)??throw new KeyNotFoundException("Organization plan not found.");
                if(saved.Approval!=PlanApprovalState.Approved||now>=saved.Plan.ExpiresAt)
                    throw new InvalidOperationException("Plan is not approved or has expired.");
                var step=saved.Plan.Steps[saved.NextStepOrdinal];
                if(step.Id!=stepId||saved.CheckpointFingerprint!=beforeFingerprint)
                    throw new StaleOrganizationPlanException("Wrong step or pre-action checkpoint.");
                var after=ReadOrganizationStateWithinTransaction();
                if(after.Fingerprint==beforeFingerprint||!OrganizationPlanContract.Matches(after,step.ExpectedAfter)||
                    !UnrelatedStateStable(LoadCheckpointState(id),after,step))
                    throw new StaleOrganizationPlanException("Observed state does not match approved step outcome.");
                var next=saved.NextStepOrdinal+1;
                Execute("UPDATE organization_plans SET approval=$approval,checkpoint_fingerprint=$fingerprint,checkpoint_state=$state,next_step_ordinal=$next WHERE id=$id",
                    ("$approval",next==saved.Plan.Steps.Length?"Completed":"Approved"),("$fingerprint",after.Fingerprint),
                    ("$state",JsonSerializer.Serialize(after)),("$next",next),("$id",id.ToString("D")));
                Execute("COMMIT");
                return LoadOrganizationPlanCore(id)!;
            }
            catch{Execute("ROLLBACK");throw;}
        }
    }

    private OrganizationState LoadCheckpointState(Guid id)
    {
        using var command=_db.CreateCommand();command.CommandText="SELECT checkpoint_state FROM organization_plans WHERE id=$id";
        command.Parameters.AddWithValue("$id",id.ToString("D"));
        try{return JsonSerializer.Deserialize<OrganizationState>(command.ExecuteScalar() as string??"")??
            throw new InvalidDataException("Saved plan checkpoint is missing.");}
        catch(JsonException e){throw new InvalidDataException("Saved plan checkpoint is malformed.",e);}
    }

    private static bool UnrelatedStateStable(OrganizationState before,OrganizationState after,PlannedOrganizationStep step)
    {
        static string Json(object value)=>JsonSerializer.Serialize(value);
        if(Json(before.GameAccounts)!=Json(after.GameAccounts)||Json(before.AccountAssignments)!=Json(after.AccountAssignments)||
            Json(before.Roles)!=Json(after.Roles)||Json(before.RoleRules)!=Json(after.RoleRules)||
            Json(before.Metadata)!=Json(after.Metadata)||Json(before.Overrides)!=Json(after.Overrides)||
            Json(before.TradeEvidence)!=Json(after.TradeEvidence)||Json(before.Settings)!=Json(after.Settings)||
            Json(before.Coexistence)!=Json(after.Coexistence)||Json(before.Middlemen)!=Json(after.Middlemen)||
            Json(before.Characters.Select(x=>new{x.Name,x.InventoryState,x.BankState}))!=
            Json(after.Characters.Select(x=>new{x.Name,x.InventoryState,x.BankState})))return false;
        bool Touches(StoredItem row)=>row.Location is "Bank" or "Inventory"&&
            step.ExpectedBefore.Concat(step.ExpectedAfter).Any(x=>x.Character.Equals(row.Character,StringComparison.OrdinalIgnoreCase)&&
                x.Location==row.Location&&x.ItemKey==ItemGroups.Key(row.Item)&&
                OrganizationPlanContract.ItemFingerprint(row.Item)==OrganizationPlanContract.ItemFingerprint(step.SourceItem));
        object[] StableItems(OrganizationState state)=>state.Items.Where(x=>!Touches(x))
            .Select(x=>(object)new{x.Character,x.Location,x.Item}).ToArray();
        return Json(StableItems(before))==Json(StableItems(after));
    }
}
