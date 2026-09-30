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
Canada Central ledger copy (ZRS) are zone-redundant; the primary App Service
keeps at least two instances. The database incarnation does not change, so
restarted instances find their release and start immediately.
`-Mode ZoneFailover` forces an in-region database failover, restarts the
primary host and measures the time until `/health` answers through the primary
gateway (≤ 15 minutes).

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

## Canada East recovery host (the activation input)

The Canada East recovery application host is committed IaC, deployed only by
an explicit activation — never automatically and never by the passive
recovery parameter file:

- **Where.** A dedicated Canada East recovery resource group (never the data
  resource group), holding the passive recovery network and its own private DNS
  zones linked only to the recovery virtual network, so no private-endpoint
  record of the primary topology is overwritten.
- **What.** `main.bicep` with `profile = 'recovery'` and the sealed
  `activation` input (`dataResourceGroup` plus the same `topology` shape as the
  primary). It deploys, all non-zonal because Canada East has no availability
  zones: deny-default egress (`egress.bicep`, same allowlist), the same
  hardened App Service (`app-service.bicep`, site `<siteName>-ce`, public access
  disabled, VNet route-all), its private endpoints (`private-endpoints.bicep`:
  the recovery site, the source account, the Canada East SQL server, Key Vault,
  the Canada Central Document Intelligence resource, the key-ring account and
  both ledger copies), and the WAF v2 gateway (`application-gateway.bicep`,
  443 only, backend TLS to the private site). Its settings are the
  `CanadaEastRecovery` profile emitted by `runtime-configuration.bicep`.
- **Identity.** The existing user-assigned Portal and gateway identities from
  the data resource group; no new identity or role is created.
- **Operator prerequisites (not IaC).** The activation parameters file is the
  real release inventory, never committed; the site certificate for the public
  host name must be available to the Canada East web space before activation,
  as it is for the primary; the recovery evidence manifest must be approved for
  exactly the `CanadaEastRecovery` configuration digest; public DNS for the
  approved host name is repointed to the recovery gateway address as the final,
  manual cutover.

## Canada East regional recovery (DEP-18; target application RTO ≤ 4 h, SQL RPO ≤ 15 min)

Manual and controlled only (AC-109). Document Intelligence **never** fails over
(to Canada East, another region, another provider or a Development fallback).
The recovery host reaches the one Canada Central resource privately while it is
healthy; while it is unavailable, extraction routes answer the fixed `503`.

`pwsh ./scripts/azure/Invoke-RecoveryDrill.ps1 -Mode RegionalFailover ... -ConfirmChangeAuthority`
performs, in order, and measures from the first step:

1. require a current membership attestation in the Canada East copy (publish
   one first with `-Mode Attest`);
2. record the geo-replication lag before failover (RPO ≤ 15 minutes);
3. the Manual SQL failover-group failover to the Canada East server;
4. customer-managed failover of the source and key-ring storage accounts.
   Asynchronously replicated sources may be missing: they are never recreated;
   source-dependent work requires a new upload (AC-110);
5. activate the Canada East recovery host from the committed IaC;
6. deploy the exact approved release artifact to it;
7. require the host to stay unavailable while quarantined (new incarnation);
8. observe the new release record in the Canada East ledger — written only after
   deletion replay, session/link revocation and membership reconciliation;
9. require the first healthy response to follow that release;
10. require the host to answer through the recovery gateway;
11. report the complete elapsed interval, reconciliation included (≤ 4 h).

Key Vault availability during the outage is Microsoft-managed and best effort;
it is **not** an RTO proof, and admission verifies wrap/unwrap before release.
After release, authentication, existing review reads and non-extraction
operations resume (AC-155 to AC-157).

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
