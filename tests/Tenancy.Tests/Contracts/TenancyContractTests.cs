using System.Reflection;
using System.Text.Json.Serialization;
using CaseReview.Models;
using TaxLogicEngine.Models;
using Tenancy.Contracts;
using Tenancy.Models;
using Tenancy.Services;

namespace Tenancy.Tests.Contracts;

/// <summary>
/// The Wave 0 contract. Everything downstream codes against these signatures, so
/// they are pinned here: a later wave that quietly reshapes one would break a
/// sibling it cannot see.
/// </summary>
public sealed class TenancyContractTests
{
    private static readonly Assembly TenancyAssembly = typeof(TrustedFirmId).Assembly;

    // ------------------------------------------------- the exported surface

    [Fact]
    public void TheModuleExportsExactlyTheApprovedTypes()
    {
        // Pinned exactly, in both directions. A type that appears here without a
        // plan is scope creep; one that disappears is a broken contract.
        var expected = new[]
        {
            "Tenancy.Contracts.IAuthenticationSessionStore",
            "Tenancy.Contracts.IDevelopmentFixtureSeeder",
            "Tenancy.Contracts.IIdentityDirectory",
            "Tenancy.Contracts.IOpaqueIdentifierSource",
            "Tenancy.Contracts.IReviewRecordPurgeService",
            "Tenancy.Contracts.IReviewRecordRepository",
            "Tenancy.Models.AuthenticationSession",
            "Tenancy.Models.Firm",
            "Tenancy.Models.FirmMembership",
            "Tenancy.Models.FirmScopeExemptions",
            "Tenancy.Models.MembershipVerification",
            "Tenancy.Models.PersistedBoxAmount",
            "Tenancy.Models.PersistedLineEntry",
            "Tenancy.Models.PersistedReviewRecord",
            "Tenancy.Models.PersistedSchema",
            "Tenancy.Models.PersistedSlip",
            "Tenancy.Models.PersistedSourceBoxCode",
            "Tenancy.Models.PersistedUnmappedBoxCode",
            "Tenancy.Models.PortalUser",
            "Tenancy.Models.PurgeOutcome",
            "Tenancy.Models.SessionBoundaries",
            "Tenancy.Models.SessionEvaluation",
            "Tenancy.Models.SlipTypeCodes",
            "Tenancy.Models.TenancyErrors",
            "Tenancy.Models.TenancyFields",
            "Tenancy.Models.TrustedFirmId",
            "Tenancy.Services.MembershipVerificationPolicy",
            "Tenancy.Services.MonetaryValueGuard",
            "Tenancy.Services.OpaqueTokenGuard",
            "Tenancy.Services.RetentionPolicy",
            "Tenancy.Services.ReviewPackageProjection",
            "Tenancy.Services.SessionLifetimePolicy",
            "Tenancy.Services.TaxpayerTextGuard",
            "Tenancy.Services.UtcInstantGuard",
        };

        var actual = TenancyAssembly
            .GetExportedTypes()
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EveryInstanceTypeIsASealedClass()
    {
        // Sealed classes, never records: a record's compiler-generated ToString()
        // prints every property, which for these types means printing a
        // taxpayer's name. Each one owns a hand-written structural ToString()
        // instead.
        var openTypes = InstanceTypes()
            .Where(type => !type.IsSealed)
            .Select(type => type.FullName!)
            .ToArray();

        Assert.Empty(openTypes);
    }

    [Fact]
    public void EveryInstanceTypeDeclaresItsOwnToString()
    {
        // Declared here, implemented with the behaviour that populates the type.
        // What matters at the contract stage is that nobody inherits object's.
        var inherited = InstanceTypes()
            .Where(type => type.GetMethod(nameof(ToString), Type.EmptyTypes)!.DeclaringType != type)
            .Select(type => type.FullName!)
            .ToArray();

        Assert.Empty(inherited);
    }

    // ------------------------------------- no generic repository, no queryable

    [Fact]
    public void NoGenericRepositoryAbstractionExists()
    {
        // A generic repository is a repository nobody can constrain to a tenant.
        // Each contract here is concrete and named for its aggregate.
        var generic = AllTypes()
            .Where(type =>
                type.Name.Contains("Repository", StringComparison.Ordinal) &&
                type.IsGenericTypeDefinition)
            .Select(type => type.FullName!)
            .ToArray();

        Assert.Empty(generic);
    }

    [Fact]
    public void NoDeclaredMemberExposesAQueryableOrAnUntypedSequence()
    {
        // Handing back a queryable hands back the ability to write an unscoped
        // query, and every tenant guarantee then rests on each caller
        // remembering a firm predicate.
        var forbidden = new[] { "IQueryable", "DbSet", "IEnumerable<Object>" };

        var offenders = AllTypes()
            .SelectMany(type => type
                .GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static |
                            BindingFlags.DeclaredOnly)
                .Select(member => new
                {
                    Type = type,
                    Member = member,
                    Returned = ReturnedTypeName(member),
                }))
            .Where(entry => entry.Returned is not null &&
                            forbidden.Any(name => entry.Returned!.Contains(name, StringComparison.Ordinal)))
            .Select(entry => $"{entry.Type.FullName}.{entry.Member.Name}: {entry.Returned}")
            .ToArray();

        Assert.Empty(offenders);
    }

    // ------------------------------------------------- the firm-scope contract

    [Fact]
    public void EveryTaxpayerDataOperationRequiresATrustedFirm()
    {
        // The rule this feature exists to enforce. Save, read, and purge each
        // take a trusted firm as a required parameter, and there is no overload,
        // optional argument, or default that omits one.
        var tenantScoped = new[]
        {
            typeof(IReviewRecordRepository),
            typeof(IReviewRecordPurgeService),
        };

        foreach (var contract in tenantScoped)
        {
            foreach (var method in contract.GetMethods())
            {
                var parameters = method.GetParameters();

                Assert.True(
                    parameters.Any(parameter => parameter.ParameterType == typeof(TrustedFirmId)),
                    $"{contract.Name}.{method.Name} must take a TrustedFirmId.");

                Assert.DoesNotContain(parameters, parameter => parameter.IsOptional);
            }
        }
    }

    [Fact]
    public void TheFirmScopeExemptionListIsExactlyThreeNamedEntries()
    {
        // Three, because tenancy has exactly three bootstrap lookups and each one
        // runs before a firm is knowable. A fourth would mean something that is
        // not identity bootstrap had escaped the firm scope.
        Assert.Equal(
            new[]
            {
                "IAuthenticationSessionStore.FindAsync(Guid, CancellationToken)",
                "IIdentityDirectory.FindUserAsync(String, String, CancellationToken)",
                "IIdentityDirectory.FindActiveMembershipsAsync(Guid, CancellationToken)",
            },
            FirmScopeExemptions.All);
    }

    [Fact]
    public void EveryExemptedOperationActuallyExistsAndOmitsAFirm()
    {
        // A named exemption that matched no real method would be a rule with
        // nothing behind it, and a real firm-omitting lookup could hide beside it.
        var exempted = new (Type Contract, string Method)[]
        {
            (typeof(IAuthenticationSessionStore), nameof(IAuthenticationSessionStore.FindAsync)),
            (typeof(IIdentityDirectory), nameof(IIdentityDirectory.FindUserAsync)),
            (typeof(IIdentityDirectory), nameof(IIdentityDirectory.FindActiveMembershipsAsync)),
        };

        foreach (var (contract, methodName) in exempted)
        {
            var method = contract.GetMethod(methodName);

            Assert.NotNull(method);
            Assert.DoesNotContain(
                method!.GetParameters(),
                parameter => parameter.ParameterType == typeof(TrustedFirmId));
        }
    }

    [Fact]
    public void TheSessionLookupReturnsTheActiveFirmRatherThanAcceptingOne()
    {
        var find = typeof(IAuthenticationSessionStore)
            .GetMethod(nameof(IAuthenticationSessionStore.FindAsync))!;

        Assert.Equal(typeof(Task<AuthenticationSession?>), find.ReturnType);
        Assert.NotNull(typeof(AuthenticationSession).GetProperty(nameof(AuthenticationSession.ActiveFirmId)));
    }

    [Fact]
    public void TheOnlyFirmOmittingLookupsAreTheEnumeratedThree()
    {
        // The complement of the exemption list: nothing outside it may take an
        // identifier and return tenant-owned state without a trusted firm.
        var repositoryMethods = typeof(IReviewRecordRepository).GetMethods()
            .Concat(typeof(IReviewRecordPurgeService).GetMethods())
            .Where(method => !method.GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(TrustedFirmId)))
            .Select(method => method.Name)
            .ToArray();

        Assert.Empty(repositoryMethods);
    }

    // --------------------------------------- the record contract has no mutation

    [Fact]
    public void TheRecordRepositoryDeclaresNoUpdateOrDeletePath()
    {
        // A persisted record is immutable and a re-save creates a new version.
        // A mutation an HTTP request could reach is one that will be reached.
        var members = typeof(IReviewRecordRepository)
            .GetMethods()
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "FindAsync", "SaveAsync" }, members);
    }

    [Fact]
    public void TheSaveContractTakesAnAlreadyProjectedPersistenceRecord()
    {
        var save = typeof(IReviewRecordRepository)
            .GetMethod(nameof(IReviewRecordRepository.SaveAsync))!;

        var parameterTypes = save.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        Assert.Equal(
            new[]
            {
                typeof(TrustedFirmId),
                typeof(PersistedReviewRecord),
                typeof(CancellationToken),
            },
            parameterTypes);

        Assert.Equal(typeof(Task<ParseResult<PersistedReviewRecord>>), save.ReturnType);
        Assert.DoesNotContain(
            save.GetParameters(),
            parameter => parameter.ParameterType.Namespace?.StartsWith(
                "CaseReview",
                StringComparison.Ordinal) == true);
    }

    // ----------------------------------------------------------- the constants

    [Theory]
    [InlineData(nameof(PersistedSchema.MonetaryPrecision), PersistedSchema.MonetaryPrecision, 19)]
    [InlineData(nameof(PersistedSchema.MonetaryScale), PersistedSchema.MonetaryScale, 4)]
    [InlineData(nameof(PersistedSchema.CaseIdMaxLength), PersistedSchema.CaseIdMaxLength, 64)]
    [InlineData(nameof(PersistedSchema.ParticipantIdMaxLength), PersistedSchema.ParticipantIdMaxLength, 64)]
    [InlineData(nameof(PersistedSchema.DisplayNameMaxLength), PersistedSchema.DisplayNameMaxLength, 200)]
    [InlineData(nameof(PersistedSchema.BoxCodeMaxLength), PersistedSchema.BoxCodeMaxLength, 16)]
    [InlineData(nameof(PersistedSchema.LineCodeMaxLength), PersistedSchema.LineCodeMaxLength, 16)]
    [InlineData(nameof(PersistedSchema.SlipTypeCodeMaxLength), PersistedSchema.SlipTypeCodeMaxLength, 8)]
    public void TheSchemaBoundIsTheApprovedValue(string name, int actual, int expected) =>
        Assert.True(actual == expected, $"{name} is {actual}; the approved bound is {expected}.");

    [Fact]
    public void NoSchemaBoundIsAFiveDigitNumber()
    {
        // A bound written as a five-digit integer is indistinguishable from a
        // hardcoded CRA line number, and would defeat the scan that looks for one.
        var bounds = typeof(PersistedSchema)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(int))
            .Select(field => (int)field.GetRawConstantValue()!)
            .ToArray();

        Assert.NotEmpty(bounds);
        Assert.All(bounds, bound => Assert.True(bound < 10_000, $"{bound} has five digits."));
    }

    [Fact]
    public void TheRetentionPeriodIsTheApprovedProductDefault()
    {
        // Seven years after the exclusive end of the tax year. A product
        // decision, deliberately NOT a CRA statutory statement.
        Assert.Equal(7, RetentionPolicy.RetentionYears);
        Assert.Equal(1, RetentionPolicy.TaxYearEndOffsetYears);
    }

    [Fact]
    public void TheSessionLifetimesAreTheApprovedDurations()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), SessionLifetimePolicy.IdleTimeout);
        Assert.Equal(TimeSpan.FromHours(8), SessionLifetimePolicy.AbsoluteLifetime);
    }

    [Fact]
    public void TheOnlySlipTypeIsTheStructuralT4Code()
    {
        Assert.Equal("T4", SlipTypeCodes.T4);
        Assert.Equal(new[] { "T4" }, SlipTypeCodes.All);
    }

    [Fact]
    public void EveryErrorMessageIsAFixedConstantWithNoFormatPlaceholder()
    {
        // A template that cannot take an argument cannot echo a caller value into
        // an error that later reaches a log or a response body.
        var messages = typeof(TenancyErrors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.NotEmpty(messages);
        Assert.All(messages, message =>
        {
            Assert.DoesNotContain("{", message, StringComparison.Ordinal);
            Assert.DoesNotContain("}", message, StringComparison.Ordinal);
        });
    }

    // ------------------------------------------------------------------- PII

    [Fact]
    public void NoDeclaredMemberNamesASinConcept()
    {
        // Omission, not masking. There is no SIN property, no SIN-presence flag,
        // and no masking helper to widen later, because the value never enters
        // this module at all.
        var forbidden = new[] { "Sin", "SocialInsurance", "Masked", "Redact", "Scrub", "Sanitiz" };

        var offenders = AllTypes()
            .SelectMany(type => type
                .GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static |
                            BindingFlags.DeclaredOnly)
                .Select(member => $"{type.FullName}.{member.Name}"))
            .Where(name => forbidden.Any(token => name.Contains(token, StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryTaxpayerContentPropertyIsIgnoredByTheSerializer()
    {
        // Defence in depth against a careless direct serialization becoming a
        // taxpayer-data export.
        var guarded = new (Type Type, string Property)[]
        {
            (typeof(PersistedReviewRecord), nameof(PersistedReviewRecord.CaseId)),
            (typeof(PersistedReviewRecord), nameof(PersistedReviewRecord.ParticipantId)),
            (typeof(PersistedReviewRecord), nameof(PersistedReviewRecord.Slips)),
            (typeof(PersistedSlip), nameof(PersistedSlip.TaxpayerDisplayName)),
            (typeof(PersistedSlip), nameof(PersistedSlip.EmployerDisplayName)),
            (typeof(PersistedSlip), nameof(PersistedSlip.BoxAmounts)),
            (typeof(PersistedSlip), nameof(PersistedSlip.LineEntries)),
            (typeof(PersistedSlip), nameof(PersistedSlip.UnmappedBoxCodes)),
            (typeof(PersistedBoxAmount), nameof(PersistedBoxAmount.BoxCode)),
            (typeof(PersistedBoxAmount), nameof(PersistedBoxAmount.Amount)),
            (typeof(PersistedLineEntry), nameof(PersistedLineEntry.LineCode)),
            (typeof(PersistedLineEntry), nameof(PersistedLineEntry.Amount)),
            (typeof(PersistedSourceBoxCode), nameof(PersistedSourceBoxCode.BoxCode)),
            (typeof(PersistedUnmappedBoxCode), nameof(PersistedUnmappedBoxCode.BoxCode)),
            (typeof(PortalUser), nameof(PortalUser.Issuer)),
            (typeof(PortalUser), nameof(PortalUser.Subject)),
        };

        foreach (var (type, property) in guarded)
        {
            Assert.NotNull(type.GetProperty(property)!.GetCustomAttribute<JsonIgnoreAttribute>());
        }
    }

    [Fact]
    public void NoPersistedTypeDeclaresAContactOrIdentityDocumentProperty()
    {
        var forbidden = new[]
        {
            "Email", "Phone", "Address", "DateOfBirth", "BirthDate",
            "AccountNumber", "Registration", "SourceReference", "Token",
        };

        var offenders = AllTypes()
            .Where(type => type.Namespace == "Tenancy.Models")
            .SelectMany(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(property => $"{type.Name}.{property.Name}"))
            .Where(name => forbidden.Any(token => name.Contains(token, StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(offenders);
    }

    // ---------------------------------------------- durable behaviour contracts

    [Fact]
    public void DeclaredBehaviourMembersRetainTheirPublicContract()
    {
        // Contract tests pin callable shape, not temporary implementation state.
        // A task may replace a Wave 0 body without having to weaken this test.
        var contracts = new (Type Type, string Method, Type Result, Type[] Parameters)[]
        {
            (typeof(UtcInstantGuard), nameof(UtcInstantGuard.Validate),
                typeof(ValidationError), new[] { typeof(DateTimeOffset), typeof(string) }),
            (typeof(OpaqueTokenGuard), nameof(OpaqueTokenGuard.Validate),
                typeof(ValidationError), new[] { typeof(string), typeof(int), typeof(string) }),
            (typeof(TaxpayerTextGuard), nameof(TaxpayerTextGuard.Validate),
                typeof(ValidationError), new[] { typeof(string), typeof(int), typeof(string) }),
            (typeof(MonetaryValueGuard), nameof(MonetaryValueGuard.Validate),
                typeof(ValidationError), new[] { typeof(decimal), typeof(string) }),
            (typeof(RetentionPolicy), nameof(RetentionPolicy.ComputeRetentionUntilUtc),
                typeof(DateTimeOffset), new[] { typeof(int) }),
            (typeof(RetentionPolicy), nameof(RetentionPolicy.IsPurgeEligible),
                typeof(bool), new[] { typeof(DateTimeOffset), typeof(DateTimeOffset) }),
            (typeof(SlipTypeCodes), nameof(SlipTypeCodes.IsRecognized),
                typeof(bool), new[] { typeof(string) }),
            (typeof(TrustedFirmId), nameof(TrustedFirmId.FromGuid),
                typeof(TrustedFirmId), new[] { typeof(Guid) }),
            (typeof(TrustedFirmId), nameof(TrustedFirmId.TryParse),
                typeof(bool), new[] { typeof(string), typeof(TrustedFirmId).MakeByRefType() }),
            (typeof(TrustedFirmId), nameof(TrustedFirmId.ToCanonicalString),
                typeof(string), Type.EmptyTypes),
            (typeof(AuthenticationSession), nameof(AuthenticationSession.Create),
                typeof(ParseResult<AuthenticationSession>),
                new[] { typeof(Guid), typeof(Guid), typeof(Guid?), typeof(DateTimeOffset) }),
            (typeof(PurgeOutcome), nameof(PurgeOutcome.NoEligibleRecords),
                typeof(PurgeOutcome), Type.EmptyTypes),
            (typeof(PurgeOutcome), nameof(PurgeOutcome.RecordsPurged),
                typeof(PurgeOutcome), new[] { typeof(int), typeof(int) }),
            (typeof(SessionLifetimePolicy), nameof(SessionLifetimePolicy.CreateBoundaries),
                typeof(ParseResult<SessionBoundaries>), new[] { typeof(DateTimeOffset) }),
            (typeof(SessionLifetimePolicy), nameof(SessionLifetimePolicy.Touch),
                typeof(ParseResult<SessionBoundaries>),
                new[] { typeof(AuthenticationSession), typeof(DateTimeOffset) }),
            (typeof(SessionLifetimePolicy), nameof(SessionLifetimePolicy.Evaluate),
                typeof(SessionEvaluation),
                new[] { typeof(AuthenticationSession), typeof(DateTimeOffset) }),
            (typeof(MembershipVerificationPolicy), nameof(MembershipVerificationPolicy.Verify),
                typeof(MembershipVerification),
                new[] { typeof(PortalUser), typeof(FirmMembership), typeof(AuthenticationSession) }),
            (typeof(ReviewPackageProjection), nameof(ReviewPackageProjection.Project),
                typeof(ParseResult<PersistedReviewRecord>),
                new[]
                {
                    typeof(TrustedFirmId),
                    typeof(AccountantReviewPackage),
                    typeof(DateTimeOffset),
                    typeof(IOpaqueIdentifierSource),
                }),
        };

        foreach (var contract in contracts)
        {
            var method = contract.Type.GetMethod(contract.Method, contract.Parameters);

            Assert.NotNull(method);
            Assert.True(method.IsPublic, $"{contract.Type.Name}.{contract.Method} must stay public.");
            Assert.Equal(contract.Result, method.ReturnType);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static IEnumerable<Type> AllTypes() =>
        TenancyAssembly.GetTypes().Where(type => !type.Name.StartsWith('<'));

    private static IEnumerable<Type> InstanceTypes() =>
        AllTypes().Where(type =>
            type.IsClass &&
            !type.IsInterface &&
            !(type.IsAbstract && type.IsSealed));

    private static string? ReturnedTypeName(MemberInfo member) => member switch
    {
        MethodInfo method => FriendlyName(method.ReturnType),
        PropertyInfo property => FriendlyName(property.PropertyType),
        FieldInfo field => FriendlyName(field.FieldType),
        _ => null,
    };

    private static string FriendlyName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var arguments = string.Join(", ", type.GetGenericArguments().Select(FriendlyName));
        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return $"{name}<{arguments}>";
    }
}
