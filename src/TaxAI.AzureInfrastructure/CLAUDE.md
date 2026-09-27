# CLAUDE.md — TaxAI.AzureInfrastructure

Read the root [CLAUDE.md](../../CLAUDE.md) first. It owns repository policy,
the modular-monolith dependency rules, global PII policy, and the
spec→plan→implement workflow. This file defines the boundary of the spec 016
Azure infrastructure library as delivered by T01.

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
- `AzureInfrastructureRegistration.cs` — registers the options, the validator
  and the probe. Nothing else.

## Dependencies are exactly one project and two packages

- Project reference: `Tenancy` (production-control vocabulary, evidence shapes
  and the identity-probe port).
- Packages: `Azure.Identity` 1.21.0 and
  `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.10, each pinned
  exactly.

It references no EF Core, ASP.NET Core, Document Intelligence SDK, tax module,
`ClientIntake` or `ExtractionWorkflow`. Later slices add `ClientIntake` (T03),
`ExtractionWorkflow` (T04) and the Blob, Key Vault and Data Protection packages
only with their first working consumer; no future-only reference, package or
port is predeclared. Only the Portal may reference this library.

## Invariants

- **No tax authority.** No CRA line, box mapping, amount, rounding or tax-year
  policy. Configuration metadata never becomes tax truth.
- **No SIN or taxpayer content** in any option, refusal, log or exception. The
  library writes no log, reads no file and makes no call other than the one
  managed-identity token request.
- **Fail closed, never fall back.** A missing, malformed or unverifiable value
  is a refusal. There is no public-network, other-region, local-disk,
  Development-provider or alternate-credential fallback.
- **Passing validation is not authorization.** The Portal's
  `ProductionAdmissionGate` also requires the real production registrations,
  the verified identity, the exact artifact version and approved release
  evidence, including both provider assurances. As of T01 no deployable host
  can be admitted, because no production Document Intelligence adapter, source
  store or source reader exists yet.
