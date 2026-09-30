# Canadian recovery, backup and restore — operator runbook

Spec 016 T06 (FR-10, FR-11, FR-14; DEP-17 to DEP-20). This runbook covers zone
failure, a point-in-time SQL restore and the manual Canada East regional
recovery of **application service**. It never authorizes real-taxpayer
processing by itself: production admission still requires every DEP item,
both provider assurances and both role approvals.

## What protects a recovery

| Control | Where | What it guarantees |
|---|---|---|
| Independent control ledger | `infra/azure/modules/control-storage.bicep`, `AzureRecoveryControlStore` | Terminal destruction, genesis and release facts live **outside** the restorable database, in a Canada Central copy and the authoritative Canada East copy, each under a 90-day immutability policy. |
| Observed acknowledgement | `AzureRecoveryControlStore.AppendAsync` | A terminal fact counts only once it is written create-only **and read back** in both copies (Canada East first). A genesis/release fact needs Canada East. Storage replication is never counted. |
| Database incarnation | `IRecoveryReconciler.ReadIncarnationAsync` | A PITR restore, a copy and a geo-secondary are each a new incarnation, so a restored database never inherits a release — even if nobody declared the restore. |
| Startup quarantine | `ProductionAdmissionGate` → `RecoveryOrchestrator` | A host whose incarnation the ledger does not release does **not start** until reconciliation completes. No ingress exists while quarantined. |
| Release policy | `Tenancy.Models.RecoveryReleasePolicy` | Ledger available, fresh (≤ 15 min read), valid, its genesis older than the recovered data, the data inside the 42-day recoverable window, and a current membership attestation (≤ 4 h, newer than the data). |

### Retention arithmetic (AC-103, AC-111)

- SQL point-in-time restore: **35 days** (the maximum; `sql.bicep`).
- Microsoft keeps full, differential and log backups "for up to a week longer
  than the configured retention period" (Microsoft Learn, *Automated backups —
  Azure SQL Database*, read 2026-09-29): longest recoverable window **42 days**.
- Control ledger immutability: **90 days** [ASSUMED], strictly longer.
- No long-term retention policy exists (`PT0S` weekly/monthly/yearly). No
  seven-year raw SQL archive is created because records live seven years.

The immutability policies are created **unlocked**. Locking is irreversible;
lock each of the six container policies once the deployment is approved:

```powershell
az storage container immutability-policy lock --account-name <account> --container-name <container> --if-match <etag> --resource-group <rg>
```

`Invoke-RecoveryDrill.ps1 -Mode Posture` refuses until all six are `Locked`.

## Authorization attestation

The trusted provisioning process — not the application — publishes the
current active memberships (opaque firm/user identifiers only) to both control
accounts. The Portal identity can read attestations but never write them.

```powershell
pwsh ./scripts/azure/Invoke-RecoveryDrill.ps1 -Mode Attest -SubscriptionId <sub> -ControlAccountNames <cc>,<ce> -MembershipsPath <export.json> -ConfirmChangeAuthority
```

Publish a fresh attestation at the start of every recovery. A release refuses
without one no older than four hours and newer than the recovered data.

## Zone failure (target RTO ≤ 15 min, no committed-data loss)

Nothing to declare. SQL, the source store (GZRS), the key ring (GZRS) and the
Canada Central ledger copy (ZRS) are zone-redundant; App Service keeps at least
two instances. The database incarnation does not change, so restarted
instances find their release and start immediately. Verify `/health` and one
authenticated read; record the elapsed time.

## Point-in-time SQL restore (DEP-17, DEP-19, DEP-20)

1. Withdraw real-data admission if the restore replaces the serving database.
2. Publish a current attestation (above).
3. Restore to a **new** database; never overwrite, rename over or delete the
   original during the drill:
   `pwsh ./scripts/azure/Invoke-RecoveryDrill.ps1 -Mode SqlRestore -SubscriptionId <synthetic sub> -ResourceGroup <rg> -SqlServerName <server> -SqlDatabaseName <db> -ConfirmChangeAuthority`
4. Point a synthetic host at the restored copy. It must start **quarantined**
   (new incarnation), then the orchestrator:
   - replays every acknowledged terminal source fact (re-fences the registry row
     and re-deletes the object) and structured fact (re-deletes the review
     record, or stays quarantined if retained history references it);
   - cleans registry tombstones of every attested or replayed firm;
   - revokes every pre-recovery authentication session, client session and
     unspent intake link (expired ones stay expired; nothing is re-issued);
   - deactivates every membership the attestation does not list;
   - appends the release to Canada East.
5. Verify: a copied pre-restore cookie is refused (DEP-20), a purged record is
   gone (DEP-19), the host serves only after release. Record elapsed time.

## Canada East regional recovery (DEP-18; target application RTO ≤ 4 h, SQL RPO ≤ 15 min)

Manual and controlled only (AC-109). Document Intelligence **never** fails over
(to Canada East, another region, another provider or a Development fallback):
extraction stays unavailable until the Canada Central resource is valid again,
and extraction routes answer the fixed `503`.

1. Declare the recovery in the incident; record the start instant.
2. Publish a current attestation to the Canada East control account (Canada
   Central may be unreachable; the attestation read uses Canada East).
3. Read the failover group's replication lag; then perform the Manual SQL
   failover to the Canada East server (`-Mode RegionalFailover`). Record the
   observed structured-data loss window against the 15-minute RPO.
4. Customer-managed failover of the source and key-ring storage accounts to
   Canada East. Asynchronously replicated sources may be missing: they are
   never recreated; source-dependent work requires a new upload (AC-110).
5. Confirm Key Vault availability (Microsoft-managed failover is best effort
   and is **not** an RTO proof); wrap/unwrap is verified by admission.
6. **Pending plan amendment:** Canada East application hosting (App Service,
   WAF gateway and deny-default egress in `canadaeast`). The T02 hosting
   modules are pinned to `canadacentral`; until an amendment authorizes the
   Canada East hosting IaC, this step — and therefore the end-to-end
   application RTO measurement — cannot be executed. Record it as unexecuted.
7. Start hosts with the separately signed **`CanadaEastRecovery`** profile:
   `TaxAi:Production:Recovery:Profile = CanadaEastRecovery`, `Sql:*` naming the
   Canada East server (`Region = canadaeast`), `SourceStorage:Region =
   canadaeast`, and everything else unchanged — Document Intelligence, Key
   Vault and monitoring remain the Canada Central resources. Any other region
   for Document Intelligence is refused. The profile's evidence manifest must
   be approved for exactly this configuration digest.
8. Hosts start quarantined (new incarnation) and release only after the
   reconciliation in the restore section. Authentication, existing review
   reads and non-extraction operations then resume (AC-155 to AC-157).

No step may be skipped or reordered to meet the four-hour objective (AC-165):
there is no bypass flag, partial release or waiver of a quarantine reason.

## Quarantine reasons

| Reason | Meaning | Operator action |
|---|---|---|
| `ControlLedgerUnavailable` | The Canada East copy is unreachable | Restore connectivity; never substitute Canada Central for the full read |
| `ControlLedgerInvalid` | A malformed, misplaced or duplicated record | Security incident; do not delete (immutable) |
| `ControlGenesisMissing` | Empty ledger beside existing data (AC-115) | Separate compliance decision; never bootstrap |
| `ControlEvidenceStale` | Ledger read older than 15 minutes | Retry |
| `RecoveryIntervalUncovered` | Data older than the ledger's history or retention | Separate compliance decision |
| `RecoveredDataOutsideBackupWindow` | Data older than any legitimate backup (42 days) | Not an approved restore |
| `AuthorizationAttestationMissing` / `AuthorizationAttestationStale` | No current membership attestation | Publish a fresh attestation |
| `TerminalReplayIncomplete` / `RegistryReplayIncomplete` | A deletion could not be verified or is blocked | Retry; investigate a blocked record |
| `AccessInvalidationIncomplete` | Revocation failed | Retry |
| `ReleaseUnacknowledged` | Canada East did not acknowledge the release | Retry |
| `DatabaseIncarnationUnavailable` / `SecurityConfigurationInvalid` | Cannot identify the database, or invalid recovery settings | Fix configuration; never bypass |

## Never

- Copy production data into a non-production environment for a drill.
- Unlock, shorten or delete a control-ledger policy or record.
- Roll back a purge decision, re-issue an expired credential or restore
  plaintext files.
- Fail Document Intelligence over, or add an alternate processor.
