# Threat Model: Canadian Azure Production Infrastructure

Status: **initial — T01 admission boundary.** This document begins the threat
model required by the [PRD](prd.md) "Threat model requirements" and DEP-01. T01
records the production admission boundary it implements and the two approved
disclosure exceptions exactly as the PRD approves them. Later slices extend the
rows they own (T02 ingress/network, T03 keys, T04 source content, T05 provider,
T06 recovery, T07–T08 purge, T09 diagnostics). T10 verifies the final model and
records its approval. **This is not an approved threat model and not DEP-01
evidence.** Nothing here authorizes real-taxpayer processing.

## Assets

- Taxpayer source documents (may contain a SIN and medical receipt content).
- Structured extraction, review and intake history (no SIN in any form).
- Key material: source envelope wrapping key, Data Protection key ring, intake
  credential/fingerprint keys, SQL TDE and Blob CMKs.
- Identity: the Portal's user-assigned managed identity and accountant sessions.
- Release integrity: the exact artifact, its production configuration and the
  release evidence manifest.

## Approved disclosure exceptions (unchanged from the PRD)

| Exception | Exactly what is approved | What it never authorizes |
|---|---|---|
| Canadian gateway plaintext (Q-A1) | Technically necessary transient in-memory TLS termination/WAF processing inside the Canada Central Application Gateway WAF v2, re-encrypted over HTTPS/TLS to the private App Service | Persistence, export, logging, retention, packet/body capture, payload diagnostics, custom document/form logs, payload support bundles or plaintext forwarding |
| Canadian external processor (FR-06) | After in-process decryption, the complete document to the one approved Canada Central Document Intelligence resource, GA `2024-11-30`, `prebuilt-layout`, `keyValuePairs` | A second processor, another region, a preview API, LLM/generative extraction, provider-side human review, training, or any assumption about input-CMK coverage or the retention/deletion clock |

Both provider assurance gaps — input-CMK coverage and the exact retention/
deletion clock — are independent deployment blockers (DEP-05, AC-168,
AC-169). T01 encodes them as two separate required assurances; neither is a
default, an exception or satisfied by the other or by the DEP-05 row.

## T01 trust boundary: production admission

Entry point: process start of a deployable Portal host. Attacker: an operator
error, a compromised or careless configuration source, a mis-built artifact, or
an attacker able to alter application settings but not the release evidence
approval process.

| Threat | Mitigation implemented in T01 | Residual risk | Test / evidence owner |
|---|---|---|---|
| Host started outside Production, or an environment rename used as a switch | Host environment and profile must both be exactly `Production`; no enabling setting exists; any unknown `TaxAi:Production:*` key is refused | A correctly named but otherwise invalid host still reaches the other checks, all of which fail closed | T01 `ProductionStartupTests`, `ProductionConfigurationTests` |
| Primary or dependency outside Canada Central; foreign or recovery region substituted for primary | Primary must be `canadacentral`, recovery `canadaeast`; every dependency region `canadacentral` | Configured region is a claim; the live inventory probe (DEP-03) verifies it | T01 validator tests; DEP-03 (T10) |
| Endpoint/resource substitution (another resource, generic regional endpoint, arbitrary host, IP literal, HTTP, privatelink host) | Each endpoint must be the named resource's own host; each resource id must be in the configured subscription and resource group and of the expected type | DNS and routing are proven by T02/DEP-06, not by configuration | T01 validator tests; T02 |
| Public network exposure configured | Public network access must be `Disabled` and private endpoint `Required` for every dependency | Actual resource posture needs DEP-07 | T01 tests; DEP-07 |
| Preview or non-approved provider API/model/feature | Exact GA version, model and feature; any `preview` value anywhere refuses | Microsoft service changes need FR-08 review | T01 tests (AC-017, AC-134) |
| Development provider, local encrypted filesystem store/root/key, or the Development reader used in production | Refused by configuration shape and by inspecting actual registrations | — | T01 tests (AC-132, AC-142) |
| Missing production adapter replaced by a placeholder or permissive default | Absence of the production DI adapter, source store and source reader is itself a refusal; no deployable host can pass in T01 | Later slices must add real, verified registrations | T01 tests (AC-133); T04, T05 |
| Unprotected SQL transport or stored database credential | Strict/mandatory encryption, certificate validation, managed-identity authentication as the configured identity, no password or integrated security; Persistence independently refuses unencrypted transport at registration | TLS negotiation is proven by T03/DEP evidence | T01 tests (AC-144) |
| Wrong or absent managed identity; credential fallback | Data-plane token requested through `ManagedIdentityCredential` only; issued tenant, object, application and managed-identity resource claims must match; no chained, developer, CLI or environment credential; bounded by the validated probe deadline; re-verified on every call | A token issuance proves identity, not RBAC scope; least privilege is T02/T03/T09 | T01 `ManagedIdentityBindingTests`, `ProductionStartupTests` (AC-140, AC-149, AC-150) |
| Control-plane privilege acquired for validation | The probe requests only a Key Vault data-plane scope and calls no Azure Resource Manager endpoint | The offline check's optional live mode is an operator tool using the operator's own read-only access, never runtime | T01 boundary tests |
| Retention or retry values weakened | Exact FR-09 deadlines; finite attempts and deadlines within [ASSUMED] upper bounds | Bounds are recorded in the exact approved configuration | T01 tests (AC-145, AC-146) |
| Configuration drift or evidence reuse for another build | Configuration digest over every production key; artifact version must equal the running build's informational version; evidence and approvals must name that exact binding | Build provenance of the version stamp is a release-pipeline control (T09) | T01 tests; T09, T10 |
| Missing, stale, duplicated or unknown evidence; one role approving | All 24 DEP items Approved and unexpired, both assurances, both roles, no duplicates, closed states only; malformed manifests refuse | Evidence truth depends on the human approval process (DEP-24) | T01 `ProductionEvidencePolicyTests` (AC-151, AC-152, AC-168, AC-169) |
| Refusal output discloses configuration | One fixed message; closed codes only; no configured value, identifier or exception text in any refusal, probe result or `ToString` | — | T01 tests |
| Test-host behaviour mistaken for authorization | Development and the in-process TestServer are not evaluated and are never admitted; a started TestServer container evaluated by the gate is refused | — | T01 `StartupRefusalTests`, `ProductionStartupTests` |

## Boundaries owned by later slices

Upload through Canadian ingress, cross-firm source substitution, Blob tampering
or replay, memory-to-processor transfer, DNS/egress denial, provider leakage and
deletion, raw OCR/SIN escape, privileged access and key theft, purge races,
backup/failover resurrection, capacity loss and diagnostics drift are owned by
T02–T09 per the PRD table. No statement here implies that private networking
alone prevents a compromised authorized process from misusing an allowed
dependency; least privilege, response/payload discipline and tested denial are
separate layers.
