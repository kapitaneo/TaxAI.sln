# Key rotation, revocation and protected-key recovery

Spec 016 T03 (FR-04, AC-041–048, DEP-14). This procedure governs the
customer-managed keys, the shared Data Protection key ring and the intake
credential material. It is an operator procedure: it never runs from the
Portal, and nothing here authorizes real-taxpayer processing. Production stays
**REFUSED** until every DEP item, both provider assurances and both role
approvals are recorded for the exact release.

## What exists

| Purpose | Where | Reached by |
|---|---|---|
| Source-envelope key (per-content data keys, T04) | Key Vault key, runtime `SourceEnvelope:KeyUri` | Portal identity: wrap/unwrap on this key only |
| Data Protection key (shared key ring) | Key Vault key, runtime `DataProtection:KeyUri` | Portal identity: wrap/unwrap on this key only |
| SQL TDE protector | Key Vault key `sql-tde-protector` | Dedicated SQL TDE identity only |
| Source Blob service encryption (bound by T04) | Key Vault key `source-blob-cmk` | Bound by the T04 storage identity |
| Data Protection key ring | Private control storage account, container from `DataProtection:KeyRingContainer`, blob `key-ring.xml` | Portal identity: Blob Data Contributor on that container only |
| Intake digest and fingerprint keys | Key Vault secrets `taxai-intake-digest-key`, `taxai-intake-fingerprint-key` | Portal identity: Secrets User on those two secrets only |

All four keys are RSA-HSM 3072, non-exportable, limited to wrap/unwrap, and
rotated by Key Vault every **90 days** (`P90D`). The vault is Premium, Canada
Central, Azure RBAC only, soft delete with 90-day retention **and purge
protection**, with public network access disabled. The exact posture is the
closed contract `infra/azure/contracts/data-configuration.schema.json`, and the
Bicep modules (`key-vault.bicep`, `key-ring-storage.bicep`, `sql.bicep`) declare
exactly it.

## Rotation keeps old ciphertext readable

Every wrap records the exact key version that produced it. The source envelope
(T04) and the Data Protection key ring both unwrap with the recorded version, so
a rotation never breaks retained data, and no version is ever guessed. SQL TDE
follows the current version through the auto-rotating encryption protector.
Rotation creates a version; it never disables or deletes one.

## Retiring a key version

Retirement (disabling, then after purge protection's delay, destroying) is a
separate change. `KeyVersionUsageGuard` refuses it unless **all** hold:

- the version is not the active version;
- complete usage inventories exist from **each** source — retained ciphertext,
  the Data Protection key ring and recoverable approved backups;
- each inventory is at most 24 hours old and not future-dated; and
- no inventory references the version.

Missing, incomplete, stale or malformed evidence is a refusal, never an empty
inventory. The safe order is: rewrap or re-encrypt every retained item, let
each backup that needs the version expire, regenerate the inventories, then ask
the guard. Retirement is **not** proof that source content was purged:
disabling access and destroying ciphertext are different events.

## Emergency revocation

Incident revocation of key access requires a just-in-time (PIM) elevation —
never standing access — one of the closed justifications with an incident
reference, a reviewer who is not the requester, and a window of at most eight
hours that contains the decision. `KeyVersionUsageGuard.EvaluateEmergencyRevocation`
applies these rules. No key is ever exported in plaintext.

## Intake credential material

The two intake secrets hold canonical 43-character base64url encodings of
distinct 32-byte random values, generated inside the approved private network
and written directly to Key Vault; they never appear in a repository, ticket,
application setting or log. In Production the Portal reads them by managed
identity at startup and refuses a plaintext `TaxAi:Intake:DigestKey` or
`FingerprintKey` setting. Replacing either secret invalidates outstanding
one-time links and client sessions at the next restart, so it is a planned,
announced change with a window at least as long as the configured link and
idle-session lifetimes — never a silent rotation. Expired credentials are not
resurrected by any rotation.

## The drill (DEP-14)

```powershell
# Offline: synthetic keys, no network. Proves the drill; not deployment evidence.
pwsh ./scripts/azure/Invoke-KeyRotationDrill.ps1

# Live: one named key, one named subscription, explicit change authority,
# run from inside the approved private network.
pwsh ./scripts/azure/Invoke-KeyRotationDrill.ps1 -Mode Live `
  -SubscriptionId <id> -KeyVaultName <vault> -KeyName <key> `
  -UsageEvidencePath <inventory.json> -ConfirmChangeAuthority
```

The Live drill checks the vault posture and the 90-day policy against the
contract, wraps a synthetic value, rotates, unwraps the old ciphertext with the
old version, confirms retirement of the old version is refused against the
supplied inventory plus its own ciphertext, and confirms the old version is
still enabled. It never disables, deletes or purges. Exit code 3 means it did
not run and must be recorded as unexecuted, not passed. Only an observed Live
run against the exact release resources can satisfy DEP-14.

## Protected-key recovery

Key Vault soft delete and purge protection keep a deleted key or secret
recoverable for 90 days. Recovery restores the same versions, so wrapped data
and the key ring stay readable. A Canadian encrypted backup of the vault
objects and the four-hour recovery drill belong to the recovery slice (T06);
Microsoft-managed Key Vault failover alone is not a recovery-time proof.
