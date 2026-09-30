using Microsoft.Data.Sqlite;

namespace DAOrganizer.Core;

public enum TransferRunState
{
    Preparing,InSourceInventory,ExchangeOpen,Offered,Accepting,RecipientVerified,Banking,
    Complete,NeedsReconciliation,Failed
}

public sealed record TransferRun(Guid Id,Guid PlanId,Guid StepId,string SourceCharacter,
    string DestinationCharacter,TransferRunState State,string LastVerifiedHolder,long Quantity,
    string BeforeFingerprint,string? Reason,DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt);

public sealed partial class InventoryStore
{
    // This is a durable preflight checkpoint. It never sends a packet or changes custody.
    public TransferRun BeginTransferPreparation(Guid planId,DateTimeOffset now)
    {
        lock(_gate)
        {
            Execute("BEGIN IMMEDIATE TRANSACTION");
            try
            {
                var saved=LoadOrganizationPlanCore(planId)??throw new KeyNotFoundException("Organization plan not found.");
                if(saved.Approval!=PlanApprovalState.Approved||now>=saved.Plan.ExpiresAt)
                    throw new InvalidOperationException("Plan is not approved or has expired.");
                var step=saved.Plan.Steps[saved.NextStepOrdinal];
                if(step.Readiness!=OrganizationReadiness.Ready||step.RouteKind!=TransferRouteKind.Direct)
                    throw new InvalidOperationException("Only a ready direct step can begin preparation.");
                var state=ReadOrganizationStateWithinTransaction();
                if(state.Fingerprint!=saved.CheckpointFingerprint||
                   !OrganizationPlanContract.Matches(state,step.ExpectedBefore)||
                   !OrganizationPlanContract.MatchesSource(state,step))
                    throw new StaleOrganizationPlanException("Approved transfer step changed. Replan or reconcile.");
                using(var existing=_db.CreateCommand())
                {
                    existing.CommandText="SELECT count(*) FROM transfer_runs WHERE plan_id=$plan AND step_id=$step";
                    existing.Parameters.AddWithValue("$plan",planId.ToString("D"));
                    existing.Parameters.AddWithValue("$step",step.Id.ToString("D"));
                    if(Convert.ToInt32(existing.ExecuteScalar())!=0)
                        throw new InvalidOperationException("Transfer step already has a journal. Review it before retrying.");
                }
                var run=new TransferRun(Guid.NewGuid(),planId,step.Id,step.SourceCharacter,
                    step.DestinationCharacter,TransferRunState.Preparing,step.SourceCharacter,step.Quantity,
                    state.Fingerprint,null,now,now);
                try
                {
                    Execute("""
                        INSERT INTO transfer_runs(id,plan_id,step_id,source_character,destination_character,state,
                            last_verified_holder,quantity,before_fingerprint,reason,created_at,updated_at)
                        VALUES($id,$plan,$step,$source,$destination,'Preparing',$holder,$quantity,$fingerprint,NULL,$created,$updated)
                        """,("$id",run.Id.ToString("D")),("$plan",planId.ToString("D")),
                        ("$step",step.Id.ToString("D")),("$source",step.SourceCharacter),
                        ("$destination",step.DestinationCharacter),("$holder",step.SourceCharacter),
                        ("$quantity",step.Quantity),("$fingerprint",state.Fingerprint),
                        ("$created",now.ToString("O")),("$updated",now.ToString("O")));
                }
                catch(SqliteException e) when(e.SqliteErrorCode==19)
                {throw new InvalidOperationException("Another transfer or reconciliation already reserves this source.",e);}
                Execute("INSERT INTO transfer_events(run_id,ordinal,state,observed_at,detail) VALUES($id,0,'Preparing',$at,'Preflight passed; no packet sent')",
                    ("$id",run.Id.ToString("D")),("$at",now.ToString("O")));
                Execute("COMMIT");
                return run;
            }
            catch{Execute("ROLLBACK");throw;}
        }
    }

    public TransferRun? LoadTransferRun(Guid id)
    {
        lock(_gate)
        {
            using var command=_db.CreateCommand();
            command.CommandText="""
                SELECT plan_id,step_id,source_character,destination_character,state,last_verified_holder,
                    quantity,before_fingerprint,reason,created_at,updated_at
                FROM transfer_runs WHERE id=$id
                """;
            command.Parameters.AddWithValue("$id",id.ToString("D"));
            using var reader=command.ExecuteReader();
            if(!reader.Read())return null;
            if(!Enum.TryParse<TransferRunState>(reader.GetString(4),false,out var state)||!Enum.IsDefined(state))
                throw new InvalidDataException("Transfer journal state is malformed.");
            return new(id,Guid.Parse(reader.GetString(0)),Guid.Parse(reader.GetString(1)),
                reader.GetString(2),reader.GetString(3),state,reader.GetString(5),reader.GetInt64(6),
                reader.GetString(7),reader.IsDBNull(8)?null:reader.GetString(8),
                DateTimeOffset.Parse(reader.GetString(9)),DateTimeOffset.Parse(reader.GetString(10)));
        }
    }

    public IReadOnlyList<TransferRun> ListTransferRuns(int limit=50)
    {
        if(limit is <1 or >200)throw new ArgumentOutOfRangeException(nameof(limit));
        lock(_gate)
        {
            using var command=_db.CreateCommand();
            command.CommandText="SELECT id FROM transfer_runs ORDER BY updated_at DESC,id DESC LIMIT $limit";
            command.Parameters.AddWithValue("$limit",limit);
            using var reader=command.ExecuteReader();var ids=new List<Guid>();
            while(reader.Read())ids.Add(Guid.Parse(reader.GetString(0)));
            reader.Close();
            return ids.Select(id=>LoadTransferRun(id)!).ToArray();
        }
    }

    public TransferRun MarkTransferNeedsReconciliation(Guid id,string reason,DateTimeOffset now)
    {
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>256)
            throw new ArgumentException("Give a short local reconciliation reason.",nameof(reason));
        lock(_gate)
        {
            Execute("BEGIN IMMEDIATE TRANSACTION");
            try
            {
                var run=LoadTransferRun(id)??throw new KeyNotFoundException("Transfer journal not found.");
                if(run.State is TransferRunState.Complete or TransferRunState.Failed or TransferRunState.NeedsReconciliation)
                    throw new InvalidOperationException("Transfer is already terminal or awaiting reconciliation.");
                Execute("UPDATE transfer_runs SET state='NeedsReconciliation',reason=$reason,updated_at=$at WHERE id=$id",
                    ("$reason",reason),("$at",now.ToString("O")),("$id",id.ToString("D")));
                using var count=_db.CreateCommand();
                count.CommandText="SELECT count(*) FROM transfer_events WHERE run_id=$id";
                count.Parameters.AddWithValue("$id",id.ToString("D"));
                var ordinal=Convert.ToInt32(count.ExecuteScalar());
                Execute("INSERT INTO transfer_events(run_id,ordinal,state,observed_at,detail) VALUES($id,$order,'NeedsReconciliation',$at,$detail)",
                    ("$id",id.ToString("D")),("$order",ordinal),("$at",now.ToString("O")),("$detail",reason));
                Execute("COMMIT");
                return LoadTransferRun(id)!;
            }
            catch{Execute("ROLLBACK");throw;}
        }
    }

    public TransferRun AdvanceTransferRun(Guid id,TransferRunState expected,TransferRunState next,
        string detail,DateTimeOffset now)
    {
        if(string.IsNullOrWhiteSpace(detail)||detail.Length>256)
            throw new ArgumentException("Give a short local journal detail.",nameof(detail));
        if(!Allowed(expected,next))throw new InvalidOperationException("Transfer journal transition is not allowed.");
        lock(_gate)
        {
            Execute("BEGIN IMMEDIATE TRANSACTION");
            try
            {
                var run=LoadTransferRun(id)??throw new KeyNotFoundException("Transfer journal not found.");
                if(run.State!=expected||now<run.UpdatedAt)
                    throw new InvalidOperationException("Transfer journal changed or time moved backward.");
                if(next==TransferRunState.Complete)
                {
                    var saved=LoadOrganizationPlanCore(run.PlanId)??throw new InvalidDataException("Journal plan is missing.");
                    var step=saved.Plan.Steps.SingleOrDefault(x=>x.Id==run.StepId)
                        ??throw new InvalidDataException("Journal step is missing.");
                    if(saved.NextStepOrdinal<=step.Order)
                        throw new InvalidOperationException("Bank and plan checkpoint are not verified.");
                }
                var holder=next==TransferRunState.RecipientVerified?run.DestinationCharacter:run.LastVerifiedHolder;
                Execute("UPDATE transfer_runs SET state=$state,last_verified_holder=$holder,updated_at=$at WHERE id=$id",
                    ("$state",next.ToString()),("$holder",holder),("$at",now.ToString("O")),("$id",id.ToString("D")));
                using var count=_db.CreateCommand();
                count.CommandText="SELECT count(*) FROM transfer_events WHERE run_id=$id";
                count.Parameters.AddWithValue("$id",id.ToString("D"));
                var ordinal=Convert.ToInt32(count.ExecuteScalar());
                Execute("INSERT INTO transfer_events(run_id,ordinal,state,observed_at,detail) VALUES($id,$order,$state,$at,$detail)",
                    ("$id",id.ToString("D")),("$order",ordinal),("$state",next.ToString()),
                    ("$at",now.ToString("O")),("$detail",detail));
                Execute("COMMIT");
                return LoadTransferRun(id)!;
            }
            catch{Execute("ROLLBACK");throw;}
        }
    }

    private static bool Allowed(TransferRunState expected,TransferRunState next)=>(expected,next) switch
    {
        (TransferRunState.Preparing,TransferRunState.InSourceInventory or TransferRunState.Failed)=>true,
        (TransferRunState.InSourceInventory,TransferRunState.ExchangeOpen)=>true,
        (TransferRunState.ExchangeOpen,TransferRunState.Offered)=>true,
        (TransferRunState.Offered,TransferRunState.Accepting)=>true,
        (TransferRunState.Accepting,TransferRunState.RecipientVerified)=>true,
        (TransferRunState.RecipientVerified,TransferRunState.Banking)=>true,
        (TransferRunState.Banking,TransferRunState.Complete)=>true,
        _=>false
    };
}
