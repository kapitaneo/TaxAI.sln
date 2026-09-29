using System.Reflection;
using System.Text.Json;
using ClientIntake.Contracts;
using ClientIntake.Models;
using Tenancy.Models;

namespace ClientIntake.Tests.Lifecycle;

/// <summary>
/// Spec 016 T04 — the source registry contract (FR-03, FR-09 staged tracking).
/// <para>
/// The port and purger signatures are frozen exactly: every member takes the
/// trusted firm first and an explicit case, there is no firm-omitting overload,
/// no read of source content, no deadline or due-purge member and no member that
/// could clean up a bound object. The registration model is immutable, rejects
/// every inconsistent state, and serializes and renders no identifier or key.
/// </para>
/// </summary>
public sealed class SourceRegistryContractTests
{
    private static readonly TrustedFirmId Firm = TrustedFirmId.FromGuid(Guid.Parse("c0c0c0c0-0000-4000-8000-000000000001"));
    private static readonly Guid Case = Guid.Parse("c1c1c1c1-0000-4000-8000-000000000001");
    private static readonly Guid Resource = Guid.Parse("c2c2c2c2-0000-4000-8000-000000000001");
    private static readonly Guid Document = Guid.Parse("c3c3c3c3-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset Instant = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
    private const string Key = "synthetic-content-key-marker-000000000000001";

    private static string Signature(MethodInfo method) =>
        $"{method.ReturnType.Name} {method.Name}({string.Join(", ", method.GetParameters().Select(parameter => $"{parameter.ParameterType.Name} {parameter.Name}"))})";

    [Fact]
    public void TheRegistryPortHasExactlyTheConsumedFirmScopedMembers()
    {
        Assert.Equal(
            new[]
            {
                "Task`1 FindAsync(TrustedFirmId firm, Guid caseId, String contentKey, CancellationToken cancellationToken)",
                "Task`1 FindBoundAsync(TrustedFirmId firm, Guid caseId, Guid documentId, String contentKey, CancellationToken cancellationToken)",
                "Task`1 ListUncommittedAsync(TrustedFirmId firm, DateTimeOffset stagedBeforeUtc, Int32 maximumCount, CancellationToken cancellationToken)",
                "Task`1 RecordPurgeAsync(TrustedFirmId firm, Guid caseId, Guid resourceId, SourceObjectPurgeOutcome outcome, DateTimeOffset recordedAtUtc, CancellationToken cancellationToken)",
                "Task`1 RegisterStagedAsync(TrustedFirmId firm, Guid caseId, Guid resourceId, String contentKey, DateTimeOffset stagedAtUtc, CancellationToken cancellationToken)",
            },
            typeof(ISourceRegistryStore).GetMethods().Select(Signature).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void ThePurgerPortHasExactlyOneScopedMember()
    {
        Assert.Equal(
            new[] { "Task`1 PurgeAsync(TrustedFirmId firm, Guid caseId, Guid resourceId, CancellationToken cancellationToken)" },
            typeof(ISourceObjectPurger).GetMethods().Select(Signature));
    }

    [Fact] // No firm-omitting overload, no optional argument, no default.
    public void EveryMemberTakesTheTrustedFirmFirstAndHasNoOptionalParameter()
    {
        foreach (var method in typeof(ISourceRegistryStore).GetMethods().Concat(typeof(ISourceObjectPurger).GetMethods()))
        {
            var parameters = method.GetParameters();
            Assert.Equal(typeof(TrustedFirmId), parameters[0].ParameterType);
            Assert.All(parameters, parameter => Assert.False(parameter.IsOptional, $"{method.Name}.{parameter.Name}"));
            Assert.Equal(typeof(CancellationToken), parameters[^1].ParameterType);
        }
    }

    [Fact] // The registry is no read path, no lifecycle authority and no due purge.
    public void NoMemberReadsContentOrDecidesADeadline()
    {
        var names = typeof(ISourceRegistryStore).GetMethods()
            .Concat(typeof(ISourceObjectPurger).GetMethods())
            .Select(method => method.Name)
            .ToArray();

        Assert.DoesNotContain(names, name => name.Contains("Read", StringComparison.Ordinal) || name.Contains("Open", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.Contains("Due", StringComparison.Ordinal) || name.Contains("Deadline", StringComparison.Ordinal) || name.Contains("Retention", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.Contains("Delete", StringComparison.Ordinal) || name.Contains("Update", StringComparison.Ordinal));
        Assert.All(
            typeof(ISourceRegistryStore).GetMethods().Concat(typeof(ISourceObjectPurger).GetMethods()),
            method => Assert.DoesNotContain(method.GetParameters(), parameter => parameter.ParameterType == typeof(Stream)));
    }

    [Fact]
    public void TheClosedVocabulariesAreExact()
    {
        Assert.Equal(new[] { "Staged", "Bound", "PurgePending", "Purged" }, Enum.GetNames<SourceRegistryState>());
        Assert.Equal(new[] { "Success", "NotFound", "Conflict" }, Enum.GetNames<SourceRegistryWriteCode>());
        Assert.Equal(new[] { "Complete", "Pending", "Unavailable" }, Enum.GetNames<SourceObjectPurgeOutcome>());
        Assert.DoesNotContain(0, Enum.GetValues<SourceRegistryState>().Cast<int>());
        Assert.DoesNotContain(0, Enum.GetValues<SourceObjectPurgeOutcome>().Cast<int>());
    }

    public static TheoryData<string> ConsistentStates() => new()
    {
        "staged",
        "bound",
        "pending cleanup",
        "purged",
    };

    private static SourceRegistration Registration(string state) => state switch
    {
        "staged" => new SourceRegistration(Firm, Case, Resource, Key, SourceRegistryState.Staged, Instant, null, null, null),
        "bound" => new SourceRegistration(Firm, Case, Resource, Key, SourceRegistryState.Bound, Instant, Document, Instant, null),
        "pending cleanup" => new SourceRegistration(Firm, Case, Resource, Key, SourceRegistryState.PurgePending, Instant, null, null, Instant),
        _ => new SourceRegistration(Firm, Case, Resource, Key, SourceRegistryState.Purged, Instant, null, null, Instant),
    };

    [Theory]
    [MemberData(nameof(ConsistentStates))]
    public void AConsistentRegistrationIsAccepted(string state)
    {
        var registration = Registration(state);

        Assert.Equal(Resource, registration.ResourceId);
        Assert.Equal(Key, registration.ContentKey);
    }

    public static TheoryData<string> InconsistentStates() => new()
    {
        "empty case",
        "empty resource",
        "undefined state",
        "local staged instant",
        "staged with a document",
        "bound without a document",
        "document without a bound instant",
        "empty document",
        "pending without a purge instant",
        "staged with a purge instant",
        "local purge instant",
    };

    [Theory]
    [MemberData(nameof(InconsistentStates))]
    public void AnInconsistentRegistrationIsRefused(string scenario)
    {
        var local = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.FromHours(-5));

        Assert.Throws<ArgumentException>(() => scenario switch
        {
            "empty case" => new SourceRegistration(Firm, Guid.Empty, Resource, Key, SourceRegistryState.Staged, Instant, null, null, null),
            "empty resource" => new SourceRegistration(Firm, Case, Guid.Empty, Key, SourceRegistryState.Staged, Instant, null, null, null),
            "undefined state" => new SourceRegistration(Firm, Case, Resource, Key, (SourceRegistryState)9, Instant, null, null, null),
            "local staged instant" => new SourceRegistration(Firm, Case, Resource, Key, SourceRegistryState.Staged, local, null, null, null),
            "staged with a document" => new SourceRegistration(Firm, Case, Resource, Key, SourceRegistryState.Staged, Instant, Document, Instant, null),
            "bound without a document" => new SourceRegistration(Firm, Case, Resource, Key, SourceRegistryState.Bound, Instant, null, null, null),
            "document without a bound instant" => new SourceRegistration(Firm, Case, Resource, Key, SourceRegistryState.Bound, Instant, Document, null, null),
            "empty document" => new SourceRegistration(Firm, Case, Resource, Key, SourceRegistryState.Bound, Instant, Guid.Empty, Instant, null),
            "pending without a purge instant" => new SourceRegistration(Firm, Case, Resource, Key, SourceRegistryState.PurgePending, Instant, null, null, null),
            "staged with a purge instant" => new SourceRegistration(Firm, Case, Resource, Key, SourceRegistryState.Staged, Instant, null, null, Instant),
            _ => new SourceRegistration(Firm, Case, Resource, Key, SourceRegistryState.PurgePending, Instant, null, null, local),
        });
    }

    [Fact]
    public void AMissingFirmOrContentKeyIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new SourceRegistration(null!, Case, Resource, Key, SourceRegistryState.Staged, Instant, null, null, null));
        Assert.ThrowsAny<ArgumentException>(() => new SourceRegistration(Firm, Case, Resource, " ", SourceRegistryState.Staged, Instant, null, null, null));
    }

    [Theory]
    [MemberData(nameof(ConsistentStates))]
    public void NoIdentifierOrKeyIsSerializedOrRendered(string state)
    {
        var registration = Registration(state);

        var json = JsonSerializer.Serialize(registration);
        var rendered = registration.ToString();

        foreach (var value in new[] { Firm.ToCanonicalString(), Case.ToString(), Resource.ToString(), Document.ToString(), Key })
        {
            Assert.DoesNotContain(value, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(value, rendered, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal($"SourceRegistration({registration.State})", rendered);
    }

    [Fact]
    public void TheRegistrationIsImmutable()
    {
        Assert.True(typeof(SourceRegistration).IsSealed);
        Assert.All(
            typeof(SourceRegistration).GetProperties(),
            property => Assert.Null(property.SetMethod));
    }
}
