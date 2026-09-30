# Feature: <short name>

Keep this to the decisions another agent needs to implement and review the work. Link current source and evidence; update the plan when the decision changes.

## Goal and user-facing behavior

- Problem and expected result:
- What users see/do, including empty and error states:
- Existing behavior that must stay:

## Existing infrastructure and change shape

- Relevant current files, services, and data flow (see [architecture map](../architecture/overview.md)):
- Abstractions to extend; duplicate path to avoid:
- Architecture/API changes and their consumers:
- Data model/schema/migration changes, or **none**:

## Implementation and handoff

| Phase/task | Scope and likely files | Input/contract needed | Output and validation | Depends on |
| --- | --- | --- | --- | --- |
| 1 | | | | |

Name one owner per shared contract/schema/integration file. Split independent tasks only after their inputs are stable. Note work that must be sequential.

## Edge cases and recovery

- Stale, missing, duplicate, malformed, or conflicting data:
- Cancellation, network/protocol failure, retry and recovery behavior:
- Safety/privacy constraints and backward compatibility:

## Verification and done

- Focused unit/packet/UI cases, plus any controlled live check needed:
- Commands to run (build and relevant checks from [`AGENTS.md`](../../AGENTS.md)):
- Definition of done: observable behavior, passing checks, migration/rollback evidence if applicable, reviewable diff, and documented limits.

## Future considerations

- Deferred work and why it can wait:
