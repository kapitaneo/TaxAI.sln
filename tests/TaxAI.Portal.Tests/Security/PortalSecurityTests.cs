using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxAI.Portal.Tests.Support;

namespace TaxAI.Portal.Tests.Security;

/// <summary>
/// Proves the portal's closed security posture: the approved membership
/// selector, one opaque review selector, the exact authentication and
/// authorization files, no prohibited PII, no unapproved infrastructure, no
/// secret, and no client-side stack.
/// <para>
/// The PII protection here is <b>omission, not masking</b>. In v1 the safest
/// possible posture remains mandatory for SIN and every non-display identifier:
/// they never enter the browser projection. Taxpayer and employer display names
/// are the two explicitly approved display-PII fields; they are encoded, not
/// transformed by a second masking helper.
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
        "ClientIntakeCookie.cs",
        "ClientIntakeEndpoints.cs",
        "CryptographicIntakeCredentialSource.cs",
        "DevelopmentEncryptedIntakeContentStore.cs",
        "DevelopmentSignInEndpoints.cs",
        "ExtractionAvailabilityGate.cs",
        "ExtractionConfiguration.cs",
        "ExtractionEndpoints.cs",
        "ExtractionReviewPageModel.cs",
        "FirmEndpoints.cs",
        "IntakeConfiguration.cs",
        "IntakeEndpoints.cs",
        "IntakeHttpSecurity.cs",
        "ModuleRegistration.cs",
        "PortalAuthorization.cs",
        "PortalContent.cs",
        "PortalIdentity.cs",
        "PortalStartupGuards.cs",
        "ProductionAdmissionGate.cs",
        "ProductionConfiguration.cs",
        "Program.cs",
        "RecoveryOrchestrator.cs",
        "ReviewEndpoints.cs",
        "ReviewPageModel.cs",
        "SessionTicketStore.cs",
        "TrustedIngressConfiguration.cs",
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

    // --------------------------------------- FR-49: the exact input allow-list

    public static TheoryData<string, string, string[]> InputBindingPatterns() => new()
    {
        {
            "model binding attribute",
            @"\[From(?:Body|Query|Route|Form|Header|Services)\b",
            new[]
            {
                "ClientIntakeEndpoints.cs",
                "ExtractionEndpoints.cs",
                "FirmEndpoints.cs",
                "IntakeEndpoints.cs",
            }
        },
        {
            "request body",
            @"Request\s*\.\s*Body|\bReadFromJsonAsync\b|\bReadAsStringAsync\b",
            new[] { "ClientIntakeEndpoints.cs" }
        },
        { "request query", @"Request\s*\.\s*Query|\bQueryString\b", Array.Empty<string>() },
        {
            "request form",
            @"Request\s*\.\s*Form|\bIFormFile\b|\bIFormCollection\b",
            Array.Empty<string>()
        },
        { "request headers", @"Request\s*\.\s*Headers", Array.Empty<string>() },
        {
            "request cookies",
            @"Request\s*\.\s*Cookies|\bCookieOptions\b",
            new[] { "ClientIntakeCookie.cs", "IntakeHttpSecurity.cs" }
        },
        {
            "route parameter binding",
            @"\bRouteData\b|\bGetRouteValue\b",
            Array.Empty<string>()
        },
    };

    [Theory] // spec 011 AC-59
    [MemberData(nameof(InputBindingPatterns))]
    public void RequestInputAppearsOnlyInItsExactApprovedFile(
        string surface,
        string pattern,
        string[] approvedFiles)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var matching = ProductionSourceFiles()
            .Where(path => regex.IsMatch(string.Join("\n", ExecutableLines(path))))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var expected = approvedFiles
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            expected.SequenceEqual(matching, StringComparer.Ordinal),
            $"{surface} appears in [{string.Join(", ", matching)}], expected [{string.Join(", ", expected)}].");
    }

    [Fact] // spec 011 FR-49, AC-59
    public void CallerSuppliedInputsAreTheTwoFormNamesAndOneOpaqueReviewSelector()
    {
        var endpoints = string.Join(
            "\n",
            ExecutableLines(Path.Combine(PortalRoot, "FirmEndpoints.cs")));

        Assert.Single(
            Regex.Matches(
                endpoints,
                @"\[FromForm\s*\(\s*Name\s*=\s*""membershipId""\s*\)\]",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1))
                .Cast<Match>());
        Assert.Contains("__RequestVerificationToken", endpoints, StringComparison.Ordinal);

        foreach (var forbidden in new[]
                 {
                     "firmId", "userId", "issuer", "subject",
                     "sessionId", "caseId", "participantId",
                 })
        {
            Assert.DoesNotMatch(
                new Regex(
                    $@"\[From\w*\s*\([^]]*{forbidden}",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)),
                endpoints);
        }

        var review = string.Join(
            "\n",
            ExecutableLines(Path.Combine(PortalRoot, "ReviewEndpoints.cs")));

        Assert.Contains(
            "RoutePattern = \"/reviews/{recordId:guid}\"",
            review,
            StringComparison.Ordinal);
        Assert.DoesNotContain("[From", review, StringComparison.Ordinal);

        foreach (var tenantIdentifier in new[]
                 {
                     "firmId", "userId", "membershipId", "sessionId",
                     "caseId", "participantId", "packageId",
                 })
        {
            Assert.DoesNotContain(
                $"{{{tenantIdentifier}",
                review,
                StringComparison.OrdinalIgnoreCase);
        }
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
                     "net10.0",
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
        { "unapproved taxpayer identity field", @"\bEmployeeName\b|\bEmployerName\b|\bRecipientName\b|\bDateOfBirth\b|\bEmailAddress\b|\bPhoneNumber\b|\bAccountNumber\b|\bProviderIdentity\b" },
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

    [Fact]
    public void ApprovedDisplayPiiIsReadOnlyByTheClosedReviewProjection() // spec 012 T01
    {
        foreach (var field in new[] { "TaxpayerDisplayName", "EmployerDisplayName" })
        {
            var matching = ProductionSourceFiles()
                .Where(path => string.Join("\n", ExecutableLines(path))
                    .Contains(field, StringComparison.Ordinal))
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                field == "TaxpayerDisplayName"
                    ? new[] { "IntakeEndpoints.cs", "ReviewEndpoints.cs" }
                    : new[]
                    {
                        "ExtractionEndpoints.cs",
                        "ExtractionReviewPageModel.cs",
                        "ReviewEndpoints.cs",
                    },
                matching);
        }
    }

    [Theory]
    [InlineData("ProviderDisplayName")]
    [InlineData("PatientDisplayName")]
    public void MedicalDisplayPiiIsConfinedToTheExtractionProjection(
        string field)
    {
        var matching = ProductionSourceFiles()
            .Where(path => string.Join("\n", ExecutableLines(path))
                .Contains(field, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "ExtractionEndpoints.cs",
                "ExtractionReviewPageModel.cs",
            },
            matching);
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

    [Fact]
    public void TheHttpBoundarySuppressesFrameworkRequestAndExceptionLogging()
    {
        var program = string.Join(
            "\n",
            ExecutableLines(Path.Combine(PortalRoot, "Program.cs")));

        Assert.Contains(
            "builder.Logging.SetMinimumLevel(LogLevel.Critical)",
            program,
            StringComparison.Ordinal);
        Assert.DoesNotContain("UseExceptionHandler", program, StringComparison.Ordinal);
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
            new[]
            {
                "ClientIntakeEndpoints.cs",

                // Spec 017 T01: the Development-only sign-in declares both
                // of its routes explicitly anonymous.
                "DevelopmentSignInEndpoints.cs",
                "ExtractionEndpoints.cs",
                "FirmEndpoints.cs",
                "IntakeEndpoints.cs",
                "PortalIdentity.cs",
                "Program.cs",
                "ReviewEndpoints.cs",
            }
        },
        {
            "identity",
            @"\bClaimsPrincipal\b|\bClaimsIdentity\b|\bIdentityUser\b|\bSignInAsync\b",
            new[]
            {
                // Spec 017 T01: the Development-only sign-in hands an
                // issuer-and-subject principal to the existing cookie sign-in
                // event, exactly as firm selection does.
                "DevelopmentSignInEndpoints.cs",
                "FirmEndpoints.cs",
                "PortalAuthorization.cs",
                "PortalIdentity.cs",
                "SessionTicketStore.cs",
            }
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
    public void TheApprovedAuthenticationFilesAreExactlyTheEnumeratedSet()
    {
        // Without this, widening a row's allow-list would widen the rule rather
        // than fail against it.
        Assert.Equal(
            new[]
            {
                "ClientIntakeEndpoints.cs",
                "DevelopmentSignInEndpoints.cs",
                "ExtractionEndpoints.cs",
                "FirmEndpoints.cs",
                "IntakeEndpoints.cs",
                "PortalAuthorization.cs",
                "PortalIdentity.cs",
                "PortalStartupGuards.cs",
                "Program.cs",
                "ReviewEndpoints.cs",
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

        // The file-serving surface is no longer nil, so it is pinned instead of
        // forbidden — see TheStaticFileSurfaceIsExactlyTheTrackedStylesheets below.
        // What stays absolute is the ability to point the file server somewhere
        // else or to compose a path: a host that can build a file path is a host
        // that can serve the wrong file.
        { "file provider or serving option", @"\bStaticFileOptions\b|\bPhysicalFileProvider\b|\bFileExtensionContentTypeProvider\b|\bDirectoryBrowser\b|\bSendFileAsync\b|\bUseFileServer\b|\bUseDirectoryBrowser\b" },
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
    public static TheoryData<string, string, string[]> NarrowedInfrastructurePatterns() => new()
    {
        {
            "file system",
            @"\bSystem\.IO\b|\bFileStream\b|\bStreamReader\b|\bStreamWriter\b|\bFile\s*\.\s*[A-Z]|\bDirectory\s*\.\s*[A-Z]|\bDirectoryInfo\b|\bPath\s*\.\s*[A-Z]",
            new[]
            {
                "DevelopmentEncryptedIntakeContentStore.cs",
                "IntakeConfiguration.cs",
                "PortalIdentity.cs",
            }
        },
        {
            "system clock",
            @"DateTimeOffset\s*\.\s*UtcNow",
            new[] { "SessionTicketStore.cs" }
        },
    };

    [Theory] // spec 011 FR-20 as amended
    [MemberData(nameof(NarrowedInfrastructurePatterns))]
    public void ANarrowedInfrastructureConcernAppearsOnlyInItsOneApprovedFile(
        string kind, string pattern, string[] approvedFiles)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        var violations = ProductionSourceFiles()
            .Where(path => !approvedFiles.Contains(
                Path.GetFileName(path),
                StringComparer.Ordinal))
            .Where(path => regex.IsMatch(string.Join("\n", ExecutableLines(path))))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"{kind} pattern /{pattern}/ is approved only in "
            + $"{string.Join(", ", approvedFiles)}, and appears in: "
            + string.Join(", ", violations));
    }

    [Fact] // spec 016 T03: keys, secrets and the key ring stay behind the library.
    public void ThePortalReachesKeysSecretsAndTheKeyRingOnlyThroughTheInfrastructureLibrary()
    {
        var sdk = new Regex(
            @"\b(TokenCredential|ManagedIdentityCredential|DefaultAzureCredential|SecretClient|KeyClient|CryptographyClient|KeyResolver|BlobClient|PersistKeysToAzureBlobStorage|ProtectKeysWithAzure\w*|StorageSharedKeyCredential)\b",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        var offenders = ProductionSourceFiles()
            .Where(path => sdk.IsMatch(string.Join("\n", ExecutableLines(path))))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Empty(offenders);
        Assert.Matches(sdk, "new SecretClient(vault, credential)"); // positive control

        // The Production key-ring branch only adds the approved ring, once,
        // after the Development branch; it never removes or replaces a control.
        var identity = ExecutableLines(Path.Combine(PortalRoot, "PortalIdentity.cs")).ToArray();
        var production = Array.FindIndex(identity, line => line.Contains("Environments.Production", StringComparison.Ordinal));
        Assert.True(production > 0);
        Assert.StartsWith("else if (", identity[production], StringComparison.Ordinal);
        Assert.Single(identity, line => line.Contains("ProtectWithAzureKeyRing(", StringComparison.Ordinal));
        Assert.Single(identity, line => line.Contains("PersistKeysToFileSystem(", StringComparison.Ordinal));
        Assert.True(
            Array.FindIndex(identity, line => line.Contains("ProtectWithAzureKeyRing(", StringComparison.Ordinal)) > production);

        // Scoped to the Production key-ring branch itself: the unrelated OIDC
        // scope reset elsewhere in the file is not a key-ring control.
        var branch = new List<string>();
        var depth = 0;
        for (var index = production; index < identity.Length; index++)
        {
            branch.Add(identity[index]);
            depth += identity[index].Count(character => character == '{') - identity[index].Count(character => character == '}');
            if (index > production && depth == 0)
            {
                break;
            }
        }

        Assert.Contains(branch, line => line.Contains("ProtectWithAzureKeyRing(", StringComparison.Ordinal));
        Assert.DoesNotContain(branch, line => Regex.IsMatch(line, @"\b(Remove|Disable|Clear|Replace)\w*\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
    }

    [Theory] // spec 011 FR-20 as amended
    [InlineData(@"DateTime\s*\.\s*Now")]
    [InlineData(@"DateTime\s*\.\s*UtcNow")]
    [InlineData(@"DateTimeOffset\s*\.\s*Now")]
    public void EveryOtherClockReadingStaysForbiddenEverywhere(string pattern)
    {
        // Including in the one file allowed a clock at all. There is exactly one
        // way this host may ask what time it is.
        AssertNoProductionMatch("system clock", pattern);
    }

    [Fact]
    public void TimeProviderIsConfinedToTheCompositionAndIntakeBoundaries()
    {
        var matching = ProductionSourceFiles()
            .Where(path => Regex.IsMatch(
                string.Join("\n", ExecutableLines(path)),
                @"\bTimeProvider\b",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Spec 016 T01 adds exactly one consumer: the production admission gate
        // reads the registered clock once, to decide whether release evidence is
        // currently valid. It reads no system clock directly. Spec 016 T06 adds
        // the recovery orchestrator, which takes the registered clock for its
        // recovery instant and ledger timestamps; it reads no system clock either.
        Assert.Equal(
            new[]
            {
                "ClientIntakeEndpoints.cs",
                "ExtractionAvailabilityGate.cs",
                "ExtractionEndpoints.cs",
                "IntakeEndpoints.cs",
                "ModuleRegistration.cs",
                "ProductionAdmissionGate.cs",
                "RecoveryOrchestrator.cs",
            },
            matching);

        var gate = string.Join(
            "\n",
            ExecutableLines(Path.Combine(PortalRoot, "Production", "ProductionAdmissionGate.cs")));
        Assert.Single(
            Regex.Matches(gate, @"\bTimeProvider\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
        Assert.DoesNotMatch(
            new Regex(@"\bDateTime(?:Offset)?\s*\.\s*(?:Utc)?Now\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
            gate);
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
            identity,
            line => Regex.IsMatch(
                line,
                @"\bDirectoryInfo\b|\bPath\s*\.\s*[A-Z]",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)));

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

    /// <summary>
    /// Spec 014 T01 — replaces spec 010's absolute "no static files at all".
    /// <para>
    /// The review screen's stylesheet is no longer assembled inline, so two tracked
    /// CSS files have to be reachable. This is a tighter rule than deleting the old
    /// one: the file server is named to exactly one call in exactly one file, with
    /// no options object, no alternate provider, and no composed path — and the web
    /// root's contents are pinned separately.
    /// </para>
    /// <para>
    /// <c>MapStaticAssets()</c> is specifically refused. It registers a
    /// <c>{**path:file}</c> catch-all endpoint over the whole URL space, which turns
    /// every POST-only route's <c>405</c> into a <c>404</c> and adds an endpoint in
    /// the "neither anonymous nor authorization-required" third state spec 011
    /// forbids.
    /// </para>
    /// </summary>
    [Fact]
    public void TheStaticFileSurfaceIsExactlyTheTrackedStylesheets()
    {
        var callers = ProductionSourceFiles()
            .Where(path => Regex.IsMatch(
                string.Join("\n", ExecutableLines(path)),
                @"\bUseStaticFiles\b",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Equal(new[] { "Program.cs" }, callers);

        var program = string.Join(
            "\n",
            ExecutableLines(Path.Combine(PortalRoot, "Program.cs")));

        // Parameterless: no options, no provider, no alternate request path.
        Assert.Contains("app.UseStaticFiles();", program, StringComparison.Ordinal);
        Assert.Equal(
            1,
            Regex.Matches(
                program,
                @"\bUseStaticFiles\b",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)).Count);

        // And the endpoint-routing variant is refused outright.
        foreach (var file in ProductionSourceFiles())
        {
            Assert.DoesNotContain(
                "MapStaticAssets",
                string.Join("\n", ExecutableLines(file)),
                StringComparison.Ordinal);
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

        // Spec 014 T01 replaced the review screen's inline assembled stylesheet
        // with tracked CSS, so wwwroot now exists. The spec 010 rule that it must
        // not exist is superseded by an exact-contents rule, which is strictly
        // stronger than the blanket absence it replaces would be if merely
        // relaxed: it still forbids every client-side stack the old rule forbade —
        // no script of any kind, no bundle, no map, no npm artifact — and
        // additionally names the only two files that may be there.
        var webRoot = Path.Combine(PortalRoot, "wwwroot");

        Assert.True(Directory.Exists(webRoot), "wwwroot must exist and hold only the tracked stylesheet.");

        Assert.Equal(
            new[] { "intake.css", "portal.css" },
            Directory.EnumerateFiles(webRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(webRoot, path))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void ThePortalShipsNoHandwrittenJavaScript() // AC-42, spec 014 AC-89
    {
        var scripts = Directory
            .EnumerateFiles(PortalRoot, "*.js", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "src\\TaxAI.Portal\\Components\\Shared\\ClientQuestionnairePanel.razor.js",
            },
            scripts);

        var module = File.ReadAllText(
            Path.Combine(
                PortalRoot,
                "Components",
                "Shared",
                "ClientQuestionnairePanel.razor.js"));
        Assert.Contains(
            "body.append(\"document\", file, \"upload.bin\")",
            module,
            StringComparison.Ordinal);
        Assert.Contains(
            "X-TaxAI-Antiforgery",
            module,
            StringComparison.Ordinal);
        foreach (var forbidden in new[]
                 {
                     "firmId",
                     "caseId",
                     "participantId",
                     "documentId",
                     "contentKey",
                     "fingerprint",
                 })
        {
            Assert.DoesNotContain(
                forbidden,
                module,
                StringComparison.OrdinalIgnoreCase);
        }
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

    /// <summary>
    /// Spec 016 T02 — the trusted ingress policy is the only forwarded-header
    /// surface. It reads exactly one configuration key (to refuse the platform
    /// switch), writes no diagnostic, trusts no configurable proxy list, and
    /// honours neither forwarded host nor prefix, so no header can move the
    /// application's origin, cookie scope or sign-in callback.
    /// </summary>
    [Fact]
    public void TrustedIngressIsTheOnlyForwardedHeaderSurfaceAndCannotMoveTheOrigin()
    {
        var callers = ProductionSourceFiles()
            .Where(path => Regex.IsMatch(
                string.Join("\n", ExecutableLines(path)),
                @"\b(?:UseForwardedHeaders|ForwardedHeadersOptions|X-Forwarded)",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)))
            .Select(Path.GetFileName)
            .ToArray();
        Assert.Equal(new[] { "TrustedIngressConfiguration.cs" }, callers);

        var policy = string.Join(
            "\n",
            ExecutableLines(Path.Combine(PortalRoot, "Production", "TrustedIngressConfiguration.cs")));

        Assert.Single(Regex.Matches(policy, @"configuration\[", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
        Assert.Contains("configuration[PlatformForwardedHeadersSetting]", policy, StringComparison.Ordinal);
        Assert.Contains("ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto", policy, StringComparison.Ordinal);
        Assert.Contains("TrustedForwardLimit = 1;", policy, StringComparison.Ordinal);
        Assert.Contains("options.KnownProxies.Clear();", policy, StringComparison.Ordinal);
        Assert.Contains("options.KnownIPNetworks.Clear();", policy, StringComparison.Ordinal);
        foreach (var forbidden in new[]
                 {
                     "XForwardedHost", "XForwardedPrefix", "ForwardedHeaders.All", "KnownProxies.Add",
                     "KnownIPNetworks.Add", "ILogger", "Console.", "Debug.", "Trace.", "Environment.GetEnvironmentVariable",
                 })
        {
            Assert.DoesNotContain(forbidden, policy, StringComparison.Ordinal);
        }
    }

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

    [Fact] // Spec 016 T04, DEP-10: no portal surface can return source bytes or a source address.
    public void NoPortalSurfaceCanServeSourceBytesOrASourceAddress()
    {
        var pattern = new Regex(
            @"Results\.(File|Stream|Bytes)\(|TypedResults\.(File|Stream|Bytes)\(|SendFileAsync|FileStreamResult|PhysicalFile|GenerateSasUri|BlobSasBuilder|blob\.core\.windows\.net|\bsig=|ExtractionSourceLease|\.OpenAsync\(",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        // The approved Development reader is the one file that constructs a
        // lease; no other portal file may open or hold one.
        var offenders = ProductionSourceFiles()
            .Where(path => Path.GetFileName(path) != "DevelopmentEncryptedIntakeContentStore.cs")
            .SelectMany(path => ExecutableLines(path).Select(line => (path, line)))
            .Where(entry => pattern.IsMatch(entry.line))
            .Select(entry => $"{Path.GetFileName(entry.path)}: {entry.line}")
            .ToArray();

        Assert.True(offenders.Length == 0, string.Join(" | ", offenders));
        Assert.Contains(
            ProductionSourceFiles(),
            path => Path.GetFileName(path) == "DevelopmentEncryptedIntakeContentStore.cs");

        // Positive controls: each shape of a source-serving line is recognized.
        Assert.Matches(pattern, "return Results.File(bytes, \"application/pdf\");");
        Assert.Matches(pattern, "var url = blob.GenerateSasUri(builder);");
        Assert.Matches(pattern, "await using var lease = await reader.OpenAsync(firm, caseId, documentId, key, token);");
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
