# CLAUDE.md — TaxAI.AzureInfrastructure

Read the root [CLAUDE.md](../../CLAUDE.md) first. It owns repository policy,
the modular-monolith dependency rules, global PII policy, and the
spec→plan→implement workflow. This file defines the boundary of the spec 016
Azure infrastructure library as delivered by T01 and extended by T03.

## What this project is

`TaxAI.AzureInfrastructure` is the repository's single non-Document-Intelligence
Azure adapter library. It is a class library, not a host: the Portal is still
the only executable project and the only composition root, and it calls this
library's single registration extension, `AddTaxAiAzureInfrastructure`, for the
Production environment only.

As of spec 016 T01 it owns exactly:

- `Configuration/AzureInfrastructureOptions.cs` — the raw production runtime
  profile. Every default is `null`; there is no enabling switch and no
  free-form destination list.
- `Configuration/ProductionConfigurationValidator.cs` — the structural FR-14
  validator. It accepts only the exact approved topology: explicit Production,
  Canada Central primary and Canada East recovery, resource-bound endpoints for
  Document Intelligence, source Storage, Key Vault and SQL in the configured
  subscription and resource group, private-only posture, GA `2024-11-30` /
  `prebuilt-layout` / `keyValuePairs`, a managed-identity SQL connection with
  strict encryption and no stored credential, Canada Central monitoring with
  30-day retention, versionless Key Vault keys for the source envelope and the
  separate Data Protection key ring, the exact FR-09 source deadlines, and
  finite resilience bounds. It accumulates closed `ProductionAdmissionRefusal`
  codes and never quotes a configured value.
- `Configuration/ManagedIdentityBindingProbe.cs` — the only Azure SDK consumer
  in T01. It requests one **data-plane** Key Vault token through
  `ManagedIdentityCredential` and compares the issued tenant, object,
  application and managed-identity resource claims with the configured
  binding. It never calls Azure Resource Manager, never chains to another
  credential, re-verifies on every call, and returns only a closed outcome.
- `AzureInfrastructureRegistration.cs` — registers the options, the validator,
  the identity probe and (T03) the approved-resource probe; creates the one
  workload credential; and loads the production intake material. Nothing else.

Spec 016 T03 adds the working key-protection boundary:

- `Keys/KeyVaultContentKeyWrapper.cs` — wraps and unwraps one 32-byte content
  data key with a versionless Key Vault key using RSA-OAEP-256. A wrap records
  the exact key version Key Vault reports; an unwrap goes only to that version,
  so ciphertext wrapped before a rotation stays readable and no version is
  guessed. Every failure is one fixed `CryptographicException`. Consumed now by
  the capability probe; T04's source envelope consumes the same type.
- `Keys/KeyVersionUsageGuard.cs` — the pure key-lifecycle decisions: retirement
  of a version is refused unless it is not active and complete, current usage
  evidence from retained ciphertext, the Data Protection key ring and
  recoverable backups shows nothing needs it; emergency revocation needs
  just-in-time elevation, a closed justification with a reference, an
  independent reviewer and a bounded window; the rotation target is exactly
  `P90D`. `scripts/azure/Invoke-KeyRotationDrill.ps1` runs these decisions
  from the compiled assembly.
- `Keys/KeyVaultRuntimeMaterialProvider.cs` — reads the two fixed intake secrets
  (`taxai-intake-digest-key`, `taxai-intake-fingerprint-key`) by managed
  identity and refuses missing, disabled, not-yet-valid, expired, non-canonical
  or shared material with a closed outcome and no material.
- `Protection/AzureDataProtectionConfiguration.cs` — the shared protected key
  ring: Blob repository plus Key Vault encryptor over the configured managed
  identity, the production discriminator and a 90-day key lifetime, and the
  exact approved-ring check the portal gate applies.
- `Configuration/ApprovedResourceProbe.cs` — the data-plane probe of the two
  wrapping keys (enabled, RSA of at least 3072 bits, wrap/unwrap only,
  purge-protected, non-exportable), their wrap capability and a key-ring round
  trip, mapped onto the closed refusal vocabulary. It reads no rotation policy,
  vault property or secret and calls no Azure Resource Manager endpoint; those
  are verified by the templates and the operator drill.

## Dependencies are exactly one project and nine packages

- Project reference: `Tenancy` (production-control vocabulary, evidence shapes
  and the identity-probe port).
- Packages, each pinned exactly: `Azure.Core` 1.54.0, `Azure.Identity` 1.21.0,
  `Azure.Security.KeyVault.Keys` 4.10.1, `Azure.Security.KeyVault.Secrets`
  4.11.1, `Azure.Storage.Blobs` 12.26.0,
  `Azure.Extensions.AspNetCore.DataProtection.Blobs` 1.5.2,
  `Azure.Extensions.AspNetCore.DataProtection.Keys` 1.6.2,
  `Microsoft.AspNetCore.DataProtection` 10.0.10 and
  `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.10. The Data
  Protection integrations are pinned to the versions whose `Azure.Core`
  requirement keeps the T01 managed-identity behaviour unchanged; `Azure.Core`
  and `Azure.Storage.Blobs` are named directly by the library, so they are
  pinned rather than left to transitive resolution.

The only ASP.NET Core assemblies it may name are the two Data Protection ones;
hosting, HTTP and routing stay in the portal. It references no EF Core,
Document Intelligence SDK, tax module, `ClientIntake` or `ExtractionWorkflow`.
[ASSUMED] T03 adds no `ClientIntake` reference after all: the credential-port
implementation stays in the portal and consumes the loaded material unchanged,
so the reference would have no consumer. `ExtractionWorkflow` (T04) and later
packages arrive only with their first working consumer. Only the Portal may
reference this library.

## Invariants

- **No tax authority.** No CRA line, box mapping, amount, rounding or tax-year
  policy. Configuration metadata never becomes tax truth.
- **No SIN or taxpayer content** in any option, refusal, log or exception. The
  library writes no log and reads no file. Its only calls are the managed-
  identity token request, the Key Vault key, crypto and secret data-plane
  operations, and the key-ring Blob reads and writes, each by the configured
  managed identity.
- **No key leaves Key Vault.** Private-key operations run in Key Vault; no key is
  exported, and no key-ring key is written unwrapped.
- **Fail closed, never fall back.** A missing, malformed or unverifiable value
  is a refusal. There is no public-network, other-region, local-disk,
  Development-provider or alternate-credential fallback.
- **Passing validation is not authorization.** The Portal's
  `ProductionAdmissionGate` also requires the real production registrations,
  the verified identity, the exact artifact version and approved release
  evidence, including both provider assurances. As of T01 no deployable host
  can be admitted, because no production Document Intelligence adapter, source
  store or source reader exists yet.
