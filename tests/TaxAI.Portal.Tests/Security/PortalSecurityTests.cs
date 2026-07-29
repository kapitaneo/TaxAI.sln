using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxAI.Portal.Tests.Support;

namespace TaxAI.Portal.Tests.Security;

/// <summary>
/// T05 — proves the portal's v1 posture: no input surface, no PII, no
/// authentication, no infrastructure, no secret, and no client-side stack.
/// <para>
/// The PII protection here is <b>omission, not masking</b>. In v1 the safest
/// possible posture is available and is therefore mandatory: taxpayer data never
/// enters the process, so there is nothing to mask, and introducing a second
/// masking helper is exactly what every module's instructions forbid.
/// </para>
/// <para>
/// Source scans read <b>executable lines only</b>. The portal's documentation
/// legitimately names forbidden concepts while explaining why they are
/// forbidden. The scanner guard at the bottom proves the walk and the filter
/// both still work, so these assertions cannot pass for free.
/// </para>
/// </summary>
public sealed class PortalSecurityTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>Nine consecutive digits — the shape of an unmasked SIN.</summary>
    private static readonly Regex NineDigitRun =
        new(@"\d{9}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string PortalRoot =
        Path.Combine(RepositoryRoot, "src", "TaxAI.Portal");

    private static readonly string[] ExpectedProductionFiles =
    {
        "ModuleRegistration.cs",
        "PortalContent.cs",
        "PortalIdentity.cs",
        "PortalStartupGuards.cs",
        "Program.cs",
        "SessionTicketStore.cs",
    };

    private readonly WebApplicationFactory<Program> _factory;

    /// <remarks>
    /// This suite starts a <b>Production</b> host, so per decision B-6 it must
    /// register an approved durable Data Protection provider: outside
    /// Development the host refuses to start on the framework's machine-local
    /// key-ring fallback. The registration is additive — every assertion below
    /// is unchanged, and the fake lives in the test assembly, so none of the
    /// source scans below sees it.
    /// </remarks>
    public PortalSecurityTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services => services.AddApprovedDurableDataProtection());
        });

    public void Dispose() => _factory.Dispose();

    // ------------------------------------------------- AC-27: no input surface

    public static TheoryData<string, string> InputBindingPatterns() => new()
    {
        { "model binding attribute", @"\[From(?:Body|Query|Route|Form|Header|Services)\]" },
        { "request body", @"Request\s*\.\s*Body|\bReadFromJsonAsync\b|\bReadAsStringAsync\b" },
        { "request query", @"Request\s*\.\s*Query|\bQueryString\b" },
        { "request form", @"Request\s*\.\s*Form|\bIFormFile\b|\bIFormCollection\b" },
        { "request headers", @"Request\s*\.\s*Headers" },
        { "request cookies", @"Request\s*\.\s*Cookies|\bCookieOptions\b" },
        { "route parameter binding", @"\bRouteData\b|\bGetRouteValue\b" },
    };

    [Theory] // AC-27
    [MemberData(nameof(InputBindingPatterns))]
    public void ProductionSource_ReadsNoRequestInput(string surface, string pattern)
    {
        // v1 has no input surface at all, so there is nothing to validate and
        // nothing that can be malformed. That is the whole defence.
        AssertNoProductionMatch(surface, pattern);
    }

    // -------------------------------------- AC-28, AC-37: what goes on the wire

    [Fact]
    public async Task NeitherResponseBodyContainsANineDigitRun() // AC-37
    {
        using var client = _factory.CreateClient();

        foreach (var path in new[] { "/", "/health" })
        {
            var body = await (await client.GetAsync(path)).Content.ReadAsStringAsync();

            Assert.False(
                NineDigitRun.IsMatch(body),
                $"{path} response contains a nine-digit run: {body}");
        }
    }

    [Fact]
    public async Task TheHealthBodyDisclosesNothingAboutTheSystem() // AC-28
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // A probe endpoint that describes the system is a disclosure surface:
        // it tells an unauthenticated caller what to attack.
        foreach (var disclosure in new[]
                 {
                     Environment.MachineName,
                     Environment.UserName,
                     Environment.OSVersion.VersionString,
                     "Production",
                     "Development",
                     "net8.0",
                     "Version",
                     ":\\",
                     "TaxAI.Portal",
                 })
        {
            Assert.DoesNotContain(disclosure, body, StringComparison.OrdinalIgnoreCase);
        }

        // No timestamp of any kind, and no digits at all in the health body.
        Assert.DoesNotMatch(new Regex(@"\d"), body);
    }

    [Fact]
    public async Task NoResponseCarriesAServerIdentityHeader()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.DoesNotContain(
            response.Headers,
            header => header.Key.Equals("Server", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheKestrelServerHeaderIsSuppressedAtTheSource()
    {
        // TestServer does not use Kestrel, so the wire test above cannot prove
        // this for a real deployment. The source assertion can.
        var program = string.Join("\n", ExecutableLines(Path.Combine(PortalRoot, "Program.cs")));

        Assert.Matches(new Regex(@"AddServerHeader\s*=\s*false"), program);
    }

    // ------------------------------------------- AC-30: no diagnostic leakage

    [Fact]
    public void AnyDeveloperExceptionPageIsConfinedToDevelopment() // AC-30
    {
        // The AC permits either: absent entirely, or present behind an
        // IsDevelopment() branch. Both are safe; anything else leaks stack
        // traces to whoever asks.
        foreach (var file in ProductionSourceFiles())
        {
            var executable = string.Join("\n", ExecutableLines(file));

            if (!executable.Contains("DeveloperExceptionPage", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.Contains("IsDevelopment", executable, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnUnmatchedRouteLeaksNoDiagnosticDetail() // AC-30
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/no-such-path");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        foreach (var leak in new[] { "Exception", "StackTrace", ".cs:line", ":\\", "at TaxAI" })
        {
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------------------------------------- AC-36: no PII path

    public static TheoryData<string, string> PiiPatterns() => new()
    {
        { "SIN identifier", @"(?<![A-Za-z])Sin(?![a-z])|\bSocialInsurance\w*\b|\bRecipientIdentifier\b" },
        { "masking or redaction helper", @"\bSafeDiagnosticText\b|\bMask(?:ed|ing)?\s*\(|\bRedact\w*\b|\bScrub\w*\b|\bSanitiz\w*\b" },
        { "taxpayer name or address field", @"\bEmployeeName\b|\bEmployerName\b|\bEmployeeAddress\b|\bEmployerAddress\b|\bRecipientName\b" },
    };

    [Theory] // AC-36
    [MemberData(nameof(PiiPatterns))]
    public void ProductionSource_ContainsNoPiiToken(string kind, string pattern)
    {
        AssertNoProductionMatch(kind, pattern);
    }

    [Fact]
    public void ProductionSource_ContainsNoNineDigitLiteral() // AC-36
    {
        foreach (var file in ProductionSourceFiles())
        {
            var offending = ExecutableLines(file).Where(line => NineDigitRun.IsMatch(line)).ToArray();

            Assert.True(
                offending.Length == 0,
                $"{Path.GetFileName(file)} contains a nine-digit run: {string.Join(" | ", offending)}");
        }
    }

    // ------------------------------------------------- AC-38: no request logging

    public static TheoryData<string, string> LoggingPatterns() => new()
    {
        { "HTTP logging middleware", @"\bAddHttpLogging\b|\bUseHttpLogging\b|\bAddW3CLogging\b|\bUseW3CLogging\b" },
        { "request content in a log statement", @"Log\w*\s*\([^)]*(?:Request|Path|QueryString|Headers|Cookies|Body)" },
        { "telemetry exporter", @"\bApplicationInsights\b|\bOpenTelemetry\b|\bAddTelemetry\b" },
    };

    [Theory] // AC-38
    [MemberData(nameof(LoggingPatterns))]
    public void ProductionSource_LogsNoRequestContent(string kind, string pattern)
    {
        // What may be logged once this process holds taxpayer data is Class A
        // and reserved for the human. v1 answers it by logging nothing.
        AssertNoProductionMatch(kind, pattern);
    }

    // ------------------------- spec 011 AC-130, AC-163: the authentication surface

    /// <summary>
    /// Where each authentication concern is allowed to appear, by file.
    /// <para>
    /// Replaces spec 010's "the portal declares no authentication surface at
    /// all", superseded by FR-38–FR-49. An exact per-file allow-list is strictly
    /// stronger than the blanket exclusion would be if it were merely dropped:
    /// the old rule said only that these tokens appear nowhere, and once one
    /// must appear somewhere, "nowhere" degrades to "anywhere". This says
    /// exactly where — and still forbids every one of them in the two files that
    /// hold constants and refusals.
    /// </para>
    /// </summary>
    public static TheoryData<string, string, string[]> AuthenticationPatterns() => new()
    {
        {
            "authentication wiring",
            @"\bAddAuthentication\b|\bUseAuthentication\b|\bAddCookie\b|\bAddOpenIdConnect\b",
            new[] { "PortalIdentity.cs", "Program.cs" }
        },
        {
            "authorization wiring",
            @"\bAddAuthorization\b|\bUseAuthorization\b|\[Authorize\]|\bRequireAuthorization\b|\bAllowAnonymous\b",
            new[] { "PortalIdentity.cs", "Program.cs" }
        },
        {
            "identity",
            @"\bClaimsPrincipal\b|\bClaimsIdentity\b|\bIdentityUser\b|\bSignInAsync\b",
            new[] { "PortalIdentity.cs", "SessionTicketStore.cs" }
        },
        {
            "the durable ticket store",
            @"\bITicketStore\b|\bSessionStore\b",
            new[] { "PortalIdentity.cs", "SessionTicketStore.cs" }
        },
        {
            // The startup guards name the port to inspect which implementation
            // was registered — they resolve one, and wire none. That is the
            // whole of their business with it.
            "the session store port",
            @"\bIAuthenticationSessionStore\b",
            new[] { "PortalStartupGuards.cs", "SessionTicketStore.cs" }
        },
        {
            "key ring registration",
            @"\bAddDataProtection\b|\bPersistKeysTo\w*\b",
            new[] { "PortalIdentity.cs" }
        },
    };

    [Theory] // spec 011 AC-130
    [MemberData(nameof(AuthenticationPatterns))]
    public void EveryAuthenticationTokenLivesInItsApprovedFile(
        string kind, string pattern, string[] approvedFiles)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        var matching = ProductionSourceFiles()
            .Where(path => regex.IsMatch(string.Join("\n", ExecutableLines(path))))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var unapproved = matching.Except(approvedFiles, StringComparer.Ordinal).ToArray();

        Assert.True(
            unapproved.Length == 0,
            $"{kind} pattern /{pattern}/ appears outside its approved files: {string.Join(", ", unapproved)}");
    }

    [Fact] // spec 011 AC-130
    public void NoAuthenticationTokenReachesTheFrozenConstants()
    {
        // PortalContent.cs is the spec 010 shell and health bodies, byte for
        // byte. Nothing about authentication may reach it: those two routes are
        // anonymous, constant, and answer while everything behind them is down.
        var text = string.Join("\n", ExecutableLines(Path.Combine(PortalRoot, "PortalContent.cs")));

        foreach (var row in AuthenticationPatterns())
        {
            var pattern = (string)row[1]!;

            Assert.False(
                Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
                $"PortalContent.cs matches /{pattern}/.");
        }
    }

    [Fact] // spec 011 AC-130
    public void TheStartupGuardsWireNoAuthenticationAndOnlyInspectTheStore()
    {
        // The guards can refuse and nothing else. They name the session-store
        // port to see which implementation was registered; they must not be able
        // to register, replace, or configure one.
        var text = string.Join("\n", ExecutableLines(Path.Combine(PortalRoot, "PortalStartupGuards.cs")));

        foreach (var row in AuthenticationPatterns())
        {
            var pattern = (string)row[1]!;
            var approved = ((string[])row[2]!).Contains("PortalStartupGuards.cs", StringComparer.Ordinal);

            if (approved)
            {
                continue;
            }

            Assert.False(
                Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
                $"PortalStartupGuards.cs matches /{pattern}/.");
        }
    }

    [Fact] // spec 011 AC-130
    public void TheApprovedAuthenticationFilesAreExactlyTheFourEnumeratedOnes()
    {
        // Without this, widening a row's allow-list would widen the rule rather
        // than fail against it.
        Assert.Equal(
            new[]
            {
                "PortalIdentity.cs",
                "PortalStartupGuards.cs",
                "Program.cs",
                "SessionTicketStore.cs",
            },
            AuthenticationPatterns()
                .SelectMany(row => (string[])row[2]!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact] // spec 011 AC-163
    public void TheProductionSourceSerializesNoAuthenticationTicket()
    {
        // The whole point of the ticket store: the cookie carries a key, not a
        // payload. A serializer reached here would be the second copy of the
        // principal, and the copy that goes stale.
        AssertNoProductionMatch(
            "ticket serialization",
            @"\bTicketSerializer\b|\bTicketDataFormat\b|\bJsonSerializer\b|\bBinaryFormatter\b|\bSerializeAsync\b");
    }

    [Fact] // spec 011 AC-99, AC-165
    public void TheExternalHandlerIsConfiguredToSaveNoToken()
    {
        // Asserted at the source as well as behaviourally, because this is one
        // assignment away from putting the access, refresh, and id tokens into
        // the ticket — and a behavioural test proves it for the configuration
        // the test built, while this proves it for the one that ships.
        var identity = string.Join("\n", ExecutableLines(Path.Combine(PortalRoot, "PortalIdentity.cs")));

        Assert.Matches(new Regex(@"SaveTokens\s*=\s*false"), identity);
        Assert.DoesNotMatch(new Regex(@"SaveTokens\s*=\s*true"), identity);
    }

    // ---------------------------------------------- no infrastructure at all

    public static TheoryData<string, string> InfrastructurePatterns() => new()
    {
        { "database / ORM", @"\bDbContext\b|\bDbSet\s*<|\bSqlConnection\b|\bSystem\.Data\b|EntityFrameworkCore" },
        { "outbound HTTP", @"\bHttpClient\b|\bAddHttpClient\b|\bWebRequest\b|\bSocket\b" },
        { "OCR / LLM / cloud / email", @"\bOcr\b|\bTesseract\b|\bOpenAi\b|\bAnthropic\b|\bLlm\b|\bSmtp\b|\bMailMessage\b|\bAmazon\b|\bAzure\b|\bS3\b" },
        { "background work", @"\bAddHostedService\b|\bBackgroundService\b|\bIHostedService\b|\bTimer\b" },

        // Still absolute. A host that serves files is a host that can serve the
        // wrong one, and this project serves compile-time constants only.
        { "static files", @"\bUseStaticFiles\b|\bwwwroot\b|\bStaticFileOptions\b|\bPhysicalFileProvider\b" },
    };

    [Theory] // supports AC-40's posture at source level
    [MemberData(nameof(InfrastructurePatterns))]
    public void ProductionSource_TakesNoInfrastructureDependency(string kind, string pattern)
    {
        AssertNoProductionMatch(kind, pattern);
    }

    /// <summary>
    /// The two infrastructure concerns that stopped being absolute in spec 011,
    /// each narrowed to exactly one file rather than dropped.
    /// <para>
    /// Both replacements are strictly stronger than what they replace. The
    /// file-system row previously matched four tokens and let
    /// <c>DirectoryInfo</c>, <c>File.</c>, <c>Directory.</c>, and
    /// <c>Path.Combine</c> through unnoticed; this one catches all of them and
    /// then permits a single named site. The clock row previously banned four
    /// tokens everywhere; this one still bans three of them everywhere —
    /// including in the file that is allowed the fourth — and permits
    /// <c>DateTimeOffset.UtcNow</c> in one file, once.
    /// </para>
    /// </summary>
    public static TheoryData<string, string, string> NarrowedInfrastructurePatterns() => new()
    {
        {
            "file system",
            @"\bSystem\.IO\b|\bFileStream\b|\bStreamReader\b|\bStreamWriter\b|\bFile\s*\.\s*[A-Z]|\bDirectory\s*\.\s*[A-Z]|\bDirectoryInfo\b|\bPath\s*\.\s*[A-Z]",
            "PortalIdentity.cs"
        },
        {
            "system clock",
            @"DateTimeOffset\s*\.\s*UtcNow",
            "SessionTicketStore.cs"
        },
    };

    [Theory] // spec 011 FR-20 as amended
    [MemberData(nameof(NarrowedInfrastructurePatterns))]
    public void ANarrowedInfrastructureConcernAppearsOnlyInItsOneApprovedFile(
        string kind, string pattern, string approvedFile)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        var violations = ProductionSourceFiles()
            .Where(path => !Path.GetFileName(path).Equals(approvedFile, StringComparison.Ordinal))
            .Where(path => regex.IsMatch(string.Join("\n", ExecutableLines(path))))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"{kind} pattern /{pattern}/ is approved only in {approvedFile}, and appears in: "
            + string.Join(", ", violations));
    }

    [Theory] // spec 011 FR-20 as amended
    [InlineData(@"DateTime\s*\.\s*Now")]
    [InlineData(@"DateTime\s*\.\s*UtcNow")]
    [InlineData(@"DateTimeOffset\s*\.\s*Now")]
    [InlineData(@"\bTimeProvider\b")]
    public void EveryOtherClockReadingStaysForbiddenEverywhere(string pattern)
    {
        // Including in the one file allowed a clock at all. There is exactly one
        // way this host may ask what time it is.
        AssertNoProductionMatch("system clock", pattern);
    }

    [Fact] // spec 011 FR-20 as amended
    public void TheOneApprovedClockReadingAppearsExactlyOnce()
    {
        // A single reading, in a single private helper. Several would be several
        // "now"s inside one request, and a session evaluated against two
        // different instants is a session whose boundary depends on which line
        // asked.
        var store = string.Join("\n", ExecutableLines(Path.Combine(PortalRoot, "SessionTicketStore.cs")));

        Assert.Equal(
            1,
            Regex.Matches(
                store,
                @"DateTimeOffset\s*\.\s*UtcNow",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)).Count);
    }

    [Fact] // spec 011 FR-20 as amended
    public void TheOneApprovedFileSystemUseIsTheDeveloperKeyRing()
    {
        // Narrowed to a purpose, not merely to a file: the only reason this host
        // touches a disk is a developer's local key ring, and outside
        // Development the startup guards refuse a local key ring outright.
        var identity = ExecutableLines(Path.Combine(PortalRoot, "PortalIdentity.cs")).ToArray();

        var fileSystemLine = Assert.Single(
            identity.Where(line => Regex.IsMatch(
                line,
                @"\bDirectoryInfo\b|\bPath\s*\.\s*[A-Z]",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1))));

        // One expression, naming the content root and the ignored key-ring
        // folder, and nothing else. No configured path, no environment
        // variable, and no caller-supplied component.
        Assert.Contains(
            "Path.Combine(environment.ContentRootPath, DeveloperKeyRingFolder)",
            fileSystemLine,
            StringComparison.Ordinal);

        var joined = string.Join("\n", identity);

        Assert.Contains("PersistKeysToFileSystem", joined, StringComparison.Ordinal);
        Assert.Contains("IsDevelopment", joined, StringComparison.Ordinal);
    }

    // ------------------------------- AC-41: no secret, no non-local binding

    public static TheoryData<string> TrackedConfigurationFiles() => new()
    {
        "appsettings.json",
        Path.Combine("Properties", "launchSettings.json"),
    };

    [Theory] // AC-41
    [MemberData(nameof(TrackedConfigurationFiles))]
    public void TrackedConfiguration_CarriesNoSecret(string relativePath)
    {
        var path = Path.Combine(PortalRoot, relativePath);
        Assert.True(File.Exists(path), $"{path} must exist.");

        var text = File.ReadAllText(path);

        foreach (var secret in new[]
                 {
                     @"\bConnectionStrings?\b",
                     @"\bpassword\b",
                     @"\bsecret\b",
                     @"\bapi[-_]?key\b",
                     @"\btoken\b",
                     @"\bcertificate\b",
                     @"\bpfx\b",
                     @"\bBEGIN [A-Z ]*PRIVATE KEY\b",
                 })
        {
            Assert.False(
                Regex.IsMatch(text, secret, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)),
                $"{relativePath} matches /{secret}/, which looks like a secret.");
        }
    }

    [Theory] // AC-41
    [MemberData(nameof(TrackedConfigurationFiles))]
    public void TrackedConfiguration_BindsOnlyToLocalhost(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(PortalRoot, relativePath));

        // Hosting exposure, TLS, and deployment are deliberately out of scope
        // (PRD Open question E). v1 is a local development host only.
        foreach (Match match in Regex.Matches(
                     text, @"https?://[^""\s]+", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
        {
            var url = new Uri(match.Value);

            Assert.True(
                url.Host is "localhost" or "127.0.0.1" or "[::1]",
                $"{relativePath} declares a non-local URL: {match.Value}");
        }
    }

    // ------------------------------------------- AC-42: no client-side stack

    [Fact]
    public void TheRepositoryHasNoClientSideStack() // AC-42
    {
        // Taxpayer data must never be shipped to a browser as data. Establishing
        // that posture before a screen exists is far cheaper than retrofitting
        // it after one does.
        var offending = Directory
            .EnumerateFiles(RepositoryRoot, "*", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path) && !IsGitInternal(path))
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return name.Equals("package.json", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("package-lock.json", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("angular.json", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToArray();

        Assert.True(offending.Length == 0, $"Client-side artifacts found: {string.Join(", ", offending)}");

        Assert.False(
            Directory.Exists(Path.Combine(RepositoryRoot, "node_modules")),
            "node_modules must not exist.");

        Assert.False(
            Directory.Exists(Path.Combine(PortalRoot, "wwwroot")),
            "wwwroot must not exist: the portal serves constants, not files.");
    }

    // -------------------------------------------------------------- guard

    [Fact]
    public void TheScanner_SeesEveryProductionFileAndStillMatchesAKnownPattern()
    {
        // Without this, a broken walk or an over-eager comment filter would make
        // every negative assertion in this file pass for free.
        var files = ProductionSourceFiles();

        Assert.Equal(
            ExpectedProductionFiles,
            files.Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray());

        var allExecutable = string.Join("\n", files.SelectMany(ExecutableLines));

        Assert.True(
            allExecutable.Length > 500,
            $"Implausibly little executable source: {allExecutable.Length} chars.");

        Assert.Matches(new Regex(@"\bMapGet\b"), allExecutable);
        Assert.Matches(new Regex(@"\bAddServerHeader\b"), allExecutable);

        // And prove the filter really is removing comments, so "executable lines
        // only" is not silently scanning the whole file. The probe token moved
        // when the fixture-seeding branch made IsDevelopment executable source;
        // "disclosure surface" is written only in Program.cs's commentary.
        var raw = File.ReadAllText(Path.Combine(PortalRoot, "Program.cs"));
        Assert.Contains("disclosure surface", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("disclosure surface", allExecutable, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every assertion above is negative — it passes when a pattern finds
    /// nothing. A typo'd or over-escaped regex would therefore pass silently and
    /// forever. Each pattern is paired here with a line it MUST match, so a
    /// regex that has stopped working fails loudly instead of quietly approving
    /// whatever it was meant to catch.
    /// </summary>
    public static TheoryData<string, string> PatternsAndLinesTheyMustMatch() => new()
    {
        { @"\[From(?:Body|Query|Route|Form|Header|Services)\]", "app.MapGet(\"/x\", ([FromQuery] string q) => q);" },
        { @"Request\s*\.\s*Body|\bReadFromJsonAsync\b|\bReadAsStringAsync\b", "var body = await context.Request.Body.ReadAsStringAsync();" },
        { @"Request\s*\.\s*Form|\bIFormFile\b|\bIFormCollection\b", "app.MapPost(\"/u\", (IFormFile file) => file.Length);" },
        { @"Request\s*\.\s*Headers", "var agent = context.Request.Headers[\"User-Agent\"];" },
        { @"(?<![A-Za-z])Sin(?![a-z])|\bSocialInsurance\w*\b|\bRecipientIdentifier\b", "var sin = slip.Sin;" },
        { @"\bSafeDiagnosticText\b|\bMask(?:ed|ing)?\s*\(|\bRedact\w*\b|\bScrub\w*\b|\bSanitiz\w*\b", "return Mask(value);" },
        { @"\bAddHttpLogging\b|\bUseHttpLogging\b|\bAddW3CLogging\b|\bUseW3CLogging\b", "builder.Services.AddHttpLogging(o => { });" },
        { @"Log\w*\s*\([^)]*(?:Request|Path|QueryString|Headers|Cookies|Body)", "logger.LogInformation(\"path {Path}\", context.Request.Path);" },
        { @"\bAddAuthentication\b|\bUseAuthentication\b|\bAddCookie\b|\bAddOpenIdConnect\b", "builder.Services.AddAuthentication();" },
        { @"\bAddAuthorization\b|\bUseAuthorization\b|\[Authorize\]|\bRequireAuthorization\b|\bAllowAnonymous\b", "app.MapGet(\"/x\", () => 1).RequireAuthorization();" },
        { @"\bClaimsPrincipal\b|\bClaimsIdentity\b|\bIdentityUser\b|\bSignInAsync\b", "var user = context.User as ClaimsPrincipal;" },
        { @"\bITicketStore\b|\bSessionStore\b|\bIAuthenticationSessionStore\b", "options.SessionStore = store;" },
        { @"\bAddDataProtection\b|\bPersistKeysTo\w*\b", "services.AddDataProtection().PersistKeysToFileSystem(directory);" },
        { @"\bTicketSerializer\b|\bTicketDataFormat\b|\bJsonSerializer\b|\bBinaryFormatter\b|\bSerializeAsync\b", "var blob = TicketSerializer.Default.Serialize(ticket);" },
        { @"\bDbContext\b|\bDbSet\s*<|\bSqlConnection\b|\bSystem\.Data\b|EntityFrameworkCore", "public sealed class AppDb : DbContext { }" },
        { @"\bHttpClient\b|\bAddHttpClient\b|\bWebRequest\b|\bSocket\b", "using var client = new HttpClient();" },
        { @"DateTime\s*\.\s*UtcNow", "var now = DateTime.UtcNow;" },
        { @"DateTimeOffset\s*\.\s*Now", "var now = DateTimeOffset.Now;" },
        { @"DateTimeOffset\s*\.\s*UtcNow", "var now = DateTimeOffset.UtcNow;" },
        { @"\bTimeProvider\b", "var now = TimeProvider.System.GetUtcNow();" },
        { @"\bAddHostedService\b|\bBackgroundService\b|\bIHostedService\b|\bTimer\b", "builder.Services.AddHostedService<Worker>();" },
        { @"\bUseStaticFiles\b|\bwwwroot\b|\bStaticFileOptions\b|\bPhysicalFileProvider\b", "app.UseStaticFiles();" },
        { @"\bSystem\.IO\b|\bFileStream\b|\bStreamReader\b|\bStreamWriter\b|\bFile\s*\.\s*[A-Z]|\bDirectory\s*\.\s*[A-Z]|\bDirectoryInfo\b|\bPath\s*\.\s*[A-Z]", "var keys = new DirectoryInfo(Path.Combine(root, \"keys\"));" },
    };

    [Theory]
    [MemberData(nameof(PatternsAndLinesTheyMustMatch))]
    public void EveryForbiddenPattern_ActuallyMatchesAViolatingLine(string pattern, string violatingLine)
    {
        Assert.Matches(new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)), violatingLine);
    }

    [Fact]
    public void TheNineDigitRunPattern_MatchesASinShapedLiteral()
    {
        Assert.Matches(NineDigitRun, "var planted = \"123456789\";");
        Assert.DoesNotMatch(NineDigitRun, "initial-scale=1");
    }

    // ---------------------------------------------------------------- helpers

    private static void AssertNoProductionMatch(string label, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        var violations = ProductionSourceFiles()
            .Select(path => new { Name = Path.GetFileName(path), Text = string.Join("\n", ExecutableLines(path)) })
            .Where(source => regex.IsMatch(source.Text))
            .Select(source => source.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Forbidden {label} pattern /{pattern}/ found in: {string.Join(", ", violations)}");
    }

    private static string[] ProductionSourceFiles() =>
        Directory.EnumerateFiles(PortalRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static bool IsGitInternal(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ExecutableLines(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("///", StringComparison.Ordinal) &&
                !line.StartsWith("//", StringComparison.Ordinal) &&
                !line.StartsWith("*", StringComparison.Ordinal) &&
                !line.StartsWith("/*", StringComparison.Ordinal));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the repository root containing TaxAI.sln.");
    }
}
