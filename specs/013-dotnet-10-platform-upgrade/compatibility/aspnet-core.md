# Spec 013 ASP.NET Core 9 and 10 compatibility ledger

## Sources and evidence

- [ASP.NET Core 9 breaking changes](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/9/overview?view=aspnetcore-10.0)
- [ASP.NET Core 10 breaking changes](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/overview?view=aspnetcore-10.0)

Evidence:

- **Route/auth comparator:** `RouteAuthenticationBaselineTests` compares every
  approved method/path in anonymous and authenticated states to the frozen
  .NET 8 artifact.
- **Identity/security suites:** `SignInTests`, `CookieAndTokenTests`,
  `SessionLifetimeTests`, `SessionRevocationTests`, `FirmSelectionTests`,
  `MembershipVerificationTests`, `TenantIsolationAtTheHttpBoundaryTests`,
  `PortalSecurityTests`, and `StartupRefusalTests`.
- **Source/package scan:** Portal source plus direct references contain none of
  the named absent APIs/packages.

## ASP.NET Core 9

| Official heading | Applicability and disposition | Named passing proof |
|---|---|---|
| DefaultKeyResolution.ShouldGenerateNewKey altered meaning | Not present: no custom key-manager implementation. The deployment-owned durable repository/encryptor requirement remains enforced. | `StartupRefusalTests` |
| Dev cert export no longer creates folder | Not present: no dev-certificate export automation. | Source/package scan |
| Forwarded headers middleware ignores headers from unknown proxies | Not present: no forwarded-header middleware or proxy trust configuration. | Source/package scan |
| HostBuilder enables ValidateOnBuild/ValidateScopes in Development | Verified compatible: TaxAI explicitly forces both validations in every environment, so framework defaults do not weaken production. | `ProjectStructureTests`; full Portal suite |
| Legacy Mono and Emscripten APIs not exported globally | Not present: no browser/WASM/Mono project. | Source/package scan |
| Middleware types with multiple constructors | Not present: no affected convention middleware type. | Source/package scan; build |

## ASP.NET Core 10

| Official heading | Applicability and disposition | Named passing proof |
|---|---|---|
| Cookie login redirects disabled for known API endpoints | Applicable and verified compatible. The existing endpoints do not add API metadata; status, redirect, challenge, body, authentication, and authorization remain byte/field equivalent to baseline. No endpoint metadata was added or removed to manufacture compatibility. | `RouteAuthenticationBaselineTests`; `BaselineCoversEveryRouteMethodAndAuthenticationState`; `ProjectStructureTests` |
| Deprecation of WithOpenApi | Not present: no OpenAPI registration or `WithOpenApi` call. | Source/package scan |
| Exception diagnostics suppressed when TryHandleAsync returns true | Not present: no `IExceptionHandler` implementation. Fixed safe-error/no-PII behavior remains tested. | `PortalSecurityTests`; source scan |
| IActionContextAccessor and ActionContextAccessor obsolete | Not present. | Source/package scan |
| IncludeOpenAPIAnalyzers and MVC API analyzers deprecated | Not present: no OpenAPI analyzer property or API-controller surface. | Source/package scan |
| IPNetwork and ForwardedHeadersOptions.KnownNetworks obsolete | Not present: no forwarded-header configuration. | Source/package scan |
| Microsoft.Extensions.ApiDescription.Client deprecated | Not present. | Direct package inventory |
| Razor runtime compilation obsolete | Not present: no Razor runtime compilation package/call. | Source/package scan |
| WebHostBuilder, IWebHost, and WebHost obsolete | Not present: the host uses `WebApplication.CreateBuilder`. | Build; source scan |

## Result

OIDC issuer/subject resolution, provider-token omission, provider-firm-claim
discard, secure opaque cookies, durable session authority and revocation,
firm selection, application-issued firm claims, R-1 revalidation,
antiforgery, fixed safe errors, Data Protection guards, Canadian residency
requirements, and non-Development startup refusal all pass unchanged under
ASP.NET Core 10.
