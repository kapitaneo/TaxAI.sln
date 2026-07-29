# Spec 013 ASP.NET Core 10 security equivalence verification

## Result

PASS. The .NET 10 upgrade preserves the frozen Portal route/authentication
matrix and every spec 011 identity, tenant-isolation, session, Data Protection,
PII, residency, and startup-refusal boundary.

## Route and authentication equivalence

`RouteAuthenticationBaselineTests` regenerates endpoint metadata and 26
anonymous/authenticated request results through the production OIDC, cookie,
durable-session, membership, and authorization pipeline. The current normalized
artifact is byte-equal to the frozen .NET 8 artifact:

| Artifact | SHA-256 |
|---|---|
| `evidence/route-auth.net10.json` | `ee136719879e21a100a72180cf619b58955333fdbfde6effe4e4051a7ac65cb3` |

All six endpoint metadata rows and all 13 method/path cases in both
authentication states preserve status, redirect/challenge, content type, body,
authentication, authorization, and antiforgery outcomes. No endpoint metadata
was added or removed to avoid ASP.NET Core 10 cookie behavior.

## Targeted security evidence

| Boundary | Passing proof |
|---|---|
| OIDC issuer/subject ordinal resolution | `SignInTests.TheUserIsResolvedByIssuerAndSubjectOrdinally` |
| Provider token omission and authorization-code/PKCE flow | `SignInTests.TheExternalHandlerSavesNoToken`; `CookieAndTokenTests.NeitherTheCookieNorTheSessionRecordCarriesAnOidcToken` |
| Provider-supplied firm claim discarded | `SignInTests.AProviderSuppliedFirmClaimIsDiscardedBeforeTheLocalPrincipalExists`; `CookieAndTokenTests.AFirmClaimPresentedInTheCookieIsNeverTheOneTheApplicationSees` |
| Secure opaque browser ticket | `CookieAndTokenTests` cookie flags, decrypted-ticket, clear-header, and no-value tests |
| Durable session authority and lifetime | `SessionLifetimeTests`; `CookieAndTokenTests.TheMaterializedFirmClaimEqualsTheSessionRecord` |
| Revocation and fail-closed store behavior | `SessionRevocationTests` |
| Firm selection revokes/reissues and validates membership | `FirmSelectionTests` |
| R-1 live user/membership/session agreement | `MembershipVerificationTests`; `TenantIsolationAtTheHttpBoundaryTests` |
| Antiforgery and authorization | route/auth comparator; `FirmSelectionTests.SelectionWithoutAntiforgeryIsRefusedBeforeSessionMutation` |
| Fixed safe errors and no SIN in any form | `PortalSecurityTests`; response and source scanners |
| Data Protection repository/encryptor guards | `StartupRefusalTests` |
| Durable production session store | `StartupRefusalTests.TheProductionSessionStoreIsTheDurableImplementation` |
| Canadian residency/deployment posture | tracked guidance and startup refusal; no deployment/IaC/container artifact exists |

## Production deployment refusal

The refusal remains unchanged. The repository still ships no production Azure
Data Protection provider, hosting definition, infrastructure-as-code,
production provisioning, or deployment pipeline. A non-Development host fails
closed unless a deployment-owned durable key repository and encryptor and all
other startup requirements are registered.

This build may not receive real taxpayer data in production until a separate
approved deployment specification supplies and verifies Canada
Central/Canada East hosting, the Canadian Azure Storage/Key Vault key ring,
TLS/exposure, managed identity and secrets, operations, and provisioning.
