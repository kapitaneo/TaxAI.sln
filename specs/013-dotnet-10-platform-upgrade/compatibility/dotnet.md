# Spec 013 .NET 9 and .NET 10 compatibility ledger

## Scope and evidence

Authoritative indexes reviewed on 2026-07-29:

- [.NET 9 breaking changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/9.0)
- [.NET 10 breaking changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10)

ASP.NET Core and EF Core index entries are dispositioned in their dedicated
ledgers. Each semicolon-separated heading below is a separately reviewed index
item; the stated disposition and proof apply independently to every heading in
that row.

Evidence keys:

- **S1 — source/project scan:** all solution projects and direct references,
  plus searches for named affected APIs, workloads, UI stacks, native interop,
  trimming, single-file, browser/WASM, containers, workloads, and custom
  runtime configuration.
- **S2 — repository shape:** no Docker/container, WinForms, WPF, Mono,
  Emscripten, NativeAOT, workload, `project.json`, file-based app, or central
  build/package definition exists.
- **G1 — compile/publish:** `dotnet restore TaxAI.sln`,
  `dotnet build TaxAI.sln`, and the Release Portal publish pass on SDK
  10.0.302 with no new diagnostic category.
- **G2 — behavior:** all eight test projects run with zero failures/skips and
  meet their manifest floors.
- **G3 — CLI/package:** tool restore, exact tool resolution, solution restore,
  the repository preflight, and the TRX execution gate pass.

Allowed dispositions are `not present`, `verified compatible`, and
`operationally accepted`.

## .NET 9 index

| Area | Official index headings reviewed | Applicability and disposition | Named proof |
|---|---|---|---|
| Containers | Container images no longer install zlib; .NET Monitor images simplified to version-only tags | Not present: TaxAI has no container image or .NET Monitor definition. | S2 |
| Core libraries | Adding a ZipArchiveEntry with CompressionLevel sets ZIP central directory header general-purpose bit flags; Altered UnsafeAccessor support for non-open generics; API obsoletions with custom diagnostic IDs; Ambiguous overload resolution affecting StringValues implicit operators; BigInteger maximum length; BinaryReader.ReadString returns replacement characters on malformed sequences; C# overload resolution prefers params span-type overloads; Creating an array of System.Void is not allowed; InlineArray default Equals/GetHashCode throws; EnumConverter validates registered types; FromKeyedServices no longer injects non-keyed parameters; IncrementingPollingCounter initial callback is asynchronous; Inline array struct size limit is enforced; InMemoryDirectoryInfo prepends rootDir; integer TimeSpan.From overloads; new versions of some OOB packages; RuntimeHelpers.GetSubArray returns a different type; String.Trim params ReadOnlySpan overload removed; empty environment variables supported; ZipArchiveEntry UTF8 flag handling | Named APIs/features are absent. General overload and obsoletion exposure is verified compatible by a clean compile and full behavior suite. | S1, G1, G2 |
| Cryptography | System.Security.Cryptography.Pkcs netstandard2.0 APIs removed; SafeEvpPKeyHandle.DuplicateHandle up-refs; X509Certificate2/X509Certificate constructors obsolete; Windows private-key lifetime simplified | Not present: no affected PKCS, EVP handle, certificate-constructor, or private-key-lifetime code. Data Protection behavior is covered separately by the ASP.NET/security ledger. | S1 |
| Deployment | Deprecated desktop MonoVM runtime packages; environment variables take precedence in app runtime configuration | MonoVM packages are not present. Environment configuration precedence is operationally accepted and the connection-string/Data Protection startup guards remain fail-closed. | S1, G2 |
| Interop | CET supported by default | Not present: no native binary or CET-specific configuration. | S1, S2 |
| JIT | Floating-point-to-integer conversions are saturating; some SVE APIs removed | Not present: no affected conversion or SVE intrinsic. Decimal tax behavior remains covered by the full suite. | S1, G2 |
| Networking | HttpClient metrics always report server.port; HttpClientFactory logging redacts headers by default; HttpClientFactory uses SocketsHttpHandler; HttpListenerRequest.UserAgent nullable; URI query redaction in HttpClient EventSource; URI query redaction in IHttpClientFactory logs | No HttpClientFactory/HttpListener/EventSource customization exists. Redaction changes are compatible with TaxAI's stricter no-request-value logging rule. | S1, G1, G2 |
| SDK/MSBuild | dotnet sln add rejects invalid file names; dotnet watch Hot Reload incompatibility for old frameworks; dotnet workload output changes; installer repo version no longer documented; MSBuild custom-culture resource handling; new default RID for .NET Framework; Terminal Logger default; .NET 9 SDK version requirements; warning for .NET Standard 1.x; warning for .NET 7 | TaxAI has no workloads, old target frameworks, custom cultures, .NET Framework target, or scripted parsing of these outputs. The pinned .NET 10 SDK, restore/build/test/publish commands, and solution parser pass. | S1, G1, G3 |
| Serialization | BinaryFormatter always throws; nullable JsonDocument properties deserialize as JsonValueKind.Null; System.Text.Json metadata reader unescapes metadata property names | BinaryFormatter is absent. Existing System.Text.Json contract/PII tests pass unchanged. | S1, G2 |
| Windows Forms | BindingSource.SortDescriptions non-null; nullability annotations; ComponentDesigner.Initialize validation; DataGridView accessible-name index; IMsoComponent opt-in; security analyzers; null DataGridView exception behavior; PictureBox HttpClient exceptions; StatusStrip renderer | Not present: no WinForms project or reference. | S2 |
| WPF | GetXmlNamespaceMaps type change | Not present: no WPF project or reference. | S2 |

## .NET 10 index

| Area | Official index headings reviewed | Applicability and disposition | Named proof |
|---|---|---|---|
| Containers | Default .NET images use Ubuntu | Not present: no container definition. | S2 |
| Core libraries | API obsoletions; ActivitySource Create/Start behavior; Arm64 SVE nonfaulting-load mask; BufferedStream.WriteByte flush behavior; C# 14 span overload resolution; generic-math shift consistency; W3C default trace propagator; Linux DriveInfo filesystem names; DefaultValueAttribute DynamicallyAccessedMembers removal; InlineArray explicit Size disallowed; FilePatternMatch.Stem non-null; tar atime/ctime defaults; stricter LDAP DirectoryControl parsing; MacCatalyst version normalization; runtime default termination handlers removed; System.Linq.AsyncEnumerable added to core libraries; MakeGenericSignatureType validation | Named APIs/features are absent except general overload/obsoletion exposure. Compilation, tests, and publish prove compatibility; no new diagnostic category is present. | S1, G1, G2 |
| Cryptography | CompositeMLDsa draft-08; nullable CoseSigner.Key; MLDsa/SlhDsa SecretKey rename; OpenSSL primitives unsupported on macOS; OpenSSL 1.1.1 minimum; stricter X500DistinguishedName validation; nullable X509Certificate/PublicKey key parameters; DOTNET_OPENSSL_VERSION_OVERRIDE rename | Not present: TaxAI uses none of the named APIs or environment variables and has no macOS/OpenSSL deployment definition. | S1, S2 |
| Extensions | BackgroundService ExecuteAsync entirely runs as a Task; keyed-service AnyKey fixes; configuration preserves null values; Console logger stops duplicating message; ProviderAliasAttribute assembly move; configuration trim annotations removed | No BackgroundService, AnyKey, provider alias, custom console logger, or trimmed publish exists. Configuration-dependent startup and auth behavior passes unchanged. | S1, G1, G2 |
| Globalization | DOTNET_ICU_VERSION_OVERRIDE rename | Not present. | S1 |
| Install tool | dotnet.acquire no longer always downloads latest | Not present. SDK acquisition is outside repository automation; global.json pins the resolved SDK. | S1, G3 |
| Interop | IDispatchEx-to-IReflect cast failure; single-file native-library search; AssemblyDirectory DllImport search | Not present: no COM, single-file publish, or native-library search configuration. | S1, S2 |
| Networking | HTTP/3 disabled with PublishTrimmed; stricter MailAddress consecutive-dot validation; browser HTTP response streaming; Uri length limits removed | No trimmed, browser/WASM, HTTP/3, or MailAddress surface exists. URI behavior used by OIDC/routes remains covered by route/auth tests. | S1, G2 |
| Reflection | restricted InvokeMember/FindMembers/DeclaredMembers annotations; MakeGenericSignatureType validation | Affected APIs are absent; existing reflection-based contract tests compile and pass. | S1, G1, G2 |
| SDK/MSBuild and NuGet | CLI interactive default; CLI non-command data on stderr; RID-specific tool packages; workload-set default; target-framework DefineConstants evaluation timing; native coverage instrumentation default; dnx bypasses global.json; dnx.ps1 removal; file-directive quote rejection; dotnet new defaults to SLNX; dotnet package list restores; dotnet restore audits transitive packages; local tool install creates manifest; dotnet watch logs to stderr; project.json unsupported; SHA-1 signing fingerprint deprecated; custom-build-event escape hatch removed; custom-culture resource handling; NU1510 for pruned references; packages without runtime assets omitted from deps.json; versionless PackageReference error; PrunePackageReference privatizes references; HTTP package-list/search warnings promoted; enhanced HTTP retry variable removed; invalid package IDs error; ToolCommandName behavior | Workloads, dnx, file-based apps, project.json, signing, custom culture/events, versionless references, and affected automation are absent. Restore audit, explicit package versions, local tool restore, solution parsing, build/test, and publish pass with no NU1510 or new audit warning. | S1, G1, G3 |
| Serialization | System.Text.Json property-name conflict checks; XmlSerializer no longer ignores Obsolete properties | XmlSerializer is absent. Existing JSON contracts, baselines, and PII tests pass. | S1, G2 |
| Windows Forms | API obsoletions; WPF/WinForms MenuItem and ContextMenu ambiguity; HtmlElement parameter rename; TreeView checkbox truncation; StatusStrip System RenderMode; System.Drawing exception change | Not present: no WinForms/System.Drawing project or reference. | S2 |
| WPF | empty grid definitions disallowed; invalid DynamicResource crashes | Not present: no WPF project or reference. | S2 |

## Result

No .NET 9 or .NET 10 runtime/library/SDK/deployment index item requires a
product-code compatibility change. The only repository action is the exact
.NET 10 toolchain/package preflight and documentation of the operational
deployment/rollback posture.
