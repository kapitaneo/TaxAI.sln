using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using CaseProcessing.Models;
using CaseProcessing.Services;
using DocumentExtraction.Models;
using TaxLogicEngine.Models;

namespace CaseProcessing.Tests.EndToEnd;

/// <summary>
/// T04 — the mandatory PII matrix, end to end. Covers AC-29 through AC-33.
/// <para>
/// This module is the exact moment an opaque payload becomes typed taxpayer
/// data, so the matrix is exhaustive by construction: each of the five plants is
/// placed <b>separately</b> in each of the eleven slots the contract can carry,
/// and every one of those 55 combinations is driven through the accepted pipeline
/// and through every rejection family. Nothing planted may appear in any
/// <c>ToString()</c>, any <c>ValidationError</c>, or any supported direct JSON
/// serialization — <b>omission</b>, so not even a masked remnant like
/// <c>***-**-789</c>. The one permitted SIN representation is the engine's
/// <see cref="RecipientIdentifier"/> reached through <c>T4Slip.Sin</c>.
/// </para>
/// <para>
/// The two non-numeric markers are load-bearing: a nine-digit regex can catch a
/// leaked SIN but not an echoed document id.
/// </para>
/// </summary>
public sealed class T4ProcessingPiiTests
{
    private static readonly Regex NineConsecutiveDigits = new(@"\d{9}", RegexOptions.Compiled);

    public static TheoryData<string, string> PlantMatrix()
    {
        var data = new TheoryData<string, string>();

        foreach (var slot in T4ProcessingFixture.PlantSlots)
        {
            foreach (var plant in T4ProcessingFixture.Plants)
            {
                data.Add(slot, plant);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PlantMatrix))]
    public void NoPlantedValueLeaksThroughAnyPipeline(string slot, string plant) // AC-29, AC-30
    {
        foreach (var pipeline in T4ProcessingFixture.Pipelines)
        {
            var (extraction, result) = T4ProcessingFixture.Run(pipeline, slot, plant);

            // The masked identifier, when there is one. On the omission surfaces
            // not even this may appear — that is what "omission, not masking"
            // means for the raw payload's journey through this module.
            var masked = result.Value?.Slip.Sin?.Masked;

            foreach (var rendered in T4ProcessingFixture.RenderedSurfaces(extraction, result))
            {
                Assert.DoesNotContain(plant, rendered, StringComparison.Ordinal);
                Assert.DoesNotMatch(NineConsecutiveDigits, rendered);

                if (!string.IsNullOrEmpty(masked))
                {
                    Assert.DoesNotContain(masked, rendered, StringComparison.Ordinal);
                }
            }

            // Where the engine's masking is the permitted representation, the full
            // planted value must still never survive.
            foreach (var rendered in T4ProcessingFixture.MaskedSurfaces(result))
            {
                Assert.DoesNotContain(plant, rendered, StringComparison.Ordinal);
                Assert.DoesNotMatch(NineConsecutiveDigits, rendered);
            }
        }
    }

    [Theory]
    [MemberData(nameof(PlantMatrix))]
    public void EveryPlantSlotIsReadableBackOffAnAcceptedResult(string slot, string plant)
    {
        // The reachability guard the matrix requires. Without it a mis-wired slot
        // would satisfy every "does not contain" assertion above vacuously — the
        // value would be absent because it was never carried, not because it was
        // withheld.
        var (_, result) = T4ProcessingFixture.Run("Accepted", slot, plant);

        Assert.True(result.IsSuccess);
        var value = result.Value!;

        var actual = slot switch
        {
            // Box 12 is deliberately not readable in the clear anywhere: its only
            // representation is the masked identifier, so equality against a
            // RecipientIdentifier built from the plant is the strongest readback.
            "Box12Value" => value.Slip.Sin!.Equals(new RecipientIdentifier(plant)) ? plant : "<not-carried>",
            "OtherBoxValue" => value.Slip.RppDpspRegistrationNumber,
            "EmployerName" => value.Slip.EmployerName,
            "EmployerAddress" => value.Slip.EmployerAddress,
            "EmployeeName" => value.Slip.EmployeeName,
            "EmployeeAddress" => value.Slip.EmployeeAddress,
            "DocumentId" => value.DocumentId,
            "FirmId" => value.FirmId,
            "CaseId" => value.CaseId,
            "ParticipantId" => value.ParticipantId,
            "SourceReference" => value.SourceReference,
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown plant slot."),
        };

        Assert.Equal(plant, actual);
    }

    [Fact]
    public void PlantMatrix_CoversEverySlotAndEveryPlant()
    {
        // Guards the matrix itself: a shortened slot or plant list would quietly
        // shrink every theory above.
        Assert.Equal(11, T4ProcessingFixture.PlantSlots.Length);
        Assert.Equal(5, T4ProcessingFixture.Plants.Length);
        Assert.Equal(55, PlantMatrix().Count());
        Assert.Equal(11, T4ProcessingFixture.Pipelines.Length);

        Assert.Contains("123456789", T4ProcessingFixture.Plants);
        Assert.Contains("ZZMARKERZZ", T4ProcessingFixture.Plants);
        Assert.Contains("<<leak-me>>", T4ProcessingFixture.Plants);

        // Every declared pipeline actually runs, and every rejection family
        // actually rejects — otherwise the leak sweep would be testing one
        // scenario eleven times.
        foreach (var pipeline in T4ProcessingFixture.Pipelines)
        {
            var (_, result) = T4ProcessingFixture.Run(pipeline);

            if (string.Equals(pipeline, "Accepted", StringComparison.Ordinal))
            {
                Assert.True(result.IsSuccess, "The accepted pipeline must succeed.");
            }
            else
            {
                Assert.True(result.IsFailure, $"The {pipeline} pipeline must fail closed.");
                Assert.NotEmpty(result.Errors);
                Assert.Null(result.Value);
            }
        }
    }

    // ============================================== AC-29 — the accepted pipeline

    [Fact]
    public void AcceptedPipeline_WithBox12As123456789_ExposesOnlyTheMaskedIdentifier() // AC-29
    {
        var (extraction, result) = T4ProcessingFixture.Run("Accepted", "Box12Value", "123456789");

        Assert.True(result.IsSuccess);
        var value = result.Value!;

        // The SIN is reachable, and only as the masked RecipientIdentifier.
        Assert.NotNull(value.Slip.Sin);
        Assert.Equal("***-**-789", value.Slip.Sin!.Masked);
        Assert.Equal("***-**-789", value.Slip.Sin.ToString());

        foreach (var rendered in T4ProcessingFixture.RenderedSurfaces(extraction, result))
        {
            Assert.DoesNotContain("123456789", rendered, StringComparison.Ordinal);
            Assert.DoesNotMatch(NineConsecutiveDigits, rendered);
            Assert.DoesNotContain("***-**-789", rendered, StringComparison.Ordinal);
        }

        foreach (var rendered in T4ProcessingFixture.MaskedSurfaces(result))
        {
            Assert.DoesNotContain("123456789", rendered, StringComparison.Ordinal);
            Assert.DoesNotMatch(NineConsecutiveDigits, rendered);
        }

        // The one place any SIN digit is visible at all, and only the last three.
        Assert.Equal("789", new string(value.Slip.Sin.Masked.Where(char.IsDigit).ToArray()));
    }

    [Fact]
    public void AcceptedResult_JsonSerializationIsTheTaxYearAndNothingElse() // AC-29, AC-33
    {
        var (_, result) = T4ProcessingFixture.Run("Accepted", "DocumentId", "ZZMARKERZZ");

        Assert.True(result.IsSuccess);

        // The exact shape is pinned so escaping or renaming cannot make the
        // "contains no plant" assertions pass vacuously.
        Assert.Equal("{\"TaxYear\":2025}", JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public void AcceptedResult_ToStringCarriesStructuralFactsOnly() // AC-33
    {
        var (_, result) = T4ProcessingFixture.Run("Accepted");

        Assert.True(result.IsSuccess);
        var value = result.Value!;
        var rendered = value.ToString();

        foreach (var callerValue in new[] { value.DocumentId, value.FirmId, value.CaseId, value.ParticipantId, value.SourceReference })
        {
            Assert.DoesNotContain(callerValue, rendered, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(value.Slip.EmployerName, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(value.Slip.EmployeeName, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(value.Slip.Sin!.Masked, rendered, StringComparison.Ordinal);

        // Structural facts only.
        Assert.Contains("2025", rendered, StringComparison.Ordinal);
        Assert.Contains(nameof(ProcessedT4Extraction), rendered, StringComparison.Ordinal);
    }

    // ============================================ AC-31 — one SIN representation

    [Fact]
    public void Module_DeclaresNoSinPropertyNoSinTypeAndNoMaskingHelper() // AC-31
    {
        var moduleTypes = typeof(T4CaseProcessor).Assembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith("CaseProcessing", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(moduleTypes);

        // Case-sensitive and anchored to a PascalCase word start on purpose: a
        // case-insensitive "sin" matches the middle of "Processing", which every
        // type in this module is named after.
        var forbiddenNamePart = new Regex(@"(?<![A-Za-z])(Sin|Mask|Redact|Scrub|Sanitiz|Anonym)", RegexOptions.Compiled);

        foreach (var type in moduleTypes)
        {
            Assert.False(
                forbiddenNamePart.IsMatch(type.Name),
                $"{type.Name} looks like a SIN or masking type; the module must declare none.");

            var members = type
                .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(member => member is PropertyInfo or FieldInfo or MethodInfo)
                .Where(member => !member.Name.StartsWith("<", StringComparison.Ordinal));

            foreach (var member in members)
            {
                Assert.False(
                    forbiddenNamePart.IsMatch(member.Name),
                    $"{type.Name}.{member.Name} looks like a SIN or masking member; the module must declare none.");
            }
        }

        // No RecipientIdentifier is ever constructed here — Box 12 conversion is
        // T4SlipParser's job and must stay there.
        var offending = T4ProcessingFixture.ProductionSourceFiles()
            .SelectMany(T4ProcessingFixture.ExecutableLines)
            .Where(line => Regex.IsMatch(line, @"new\s+RecipientIdentifier\b"))
            .ToArray();

        Assert.Empty(offending);
    }

    [Fact]
    public void Result_ExposesExactlyOneSinBearingMember() // AC-31
    {
        var (_, result) = T4ProcessingFixture.Run("Accepted", "Box12Value", "123456789");
        Assert.True(result.IsSuccess);

        var identifiers = ReachableObjects(result.Value!)
            .OfType<RecipientIdentifier>()
            .ToArray();

        var identifier = Assert.Single(identifiers);
        Assert.Same(result.Value!.Slip.Sin, identifier);
        Assert.Equal("789", new string(identifier.Masked.Where(char.IsDigit).ToArray()));
    }

    // ========================================== AC-32 — the raw payload is absent

    [Fact]
    public void ResultPublicSurface_ExposesNoRawPayloadType() // AC-32
    {
        var forbidden = new[] { typeof(T4RawInput), typeof(T4RawBox), typeof(T4RawInput[]), typeof(IReadOnlyList<T4RawBox>) };

        var members = typeof(ProcessedT4Extraction)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        Assert.NotEmpty(members);

        foreach (var member in members)
        {
            var exposed = member switch
            {
                PropertyInfo property => new[] { property.PropertyType },
                FieldInfo field => new[] { field.FieldType },
                MethodInfo method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType).ToArray(),
                ConstructorInfo constructor => constructor.GetParameters().Select(p => p.ParameterType).ToArray(),
                _ => Array.Empty<Type>(),
            };

            foreach (var type in exposed)
            {
                Assert.DoesNotContain(type, forbidden);
                Assert.False(
                    type.IsGenericType && type.GetGenericArguments().Intersect(forbidden).Any(),
                    $"{member.Name} exposes the raw payload through {type}.");
            }
        }
    }

    [Fact]
    public void RawPayload_IsUnreachableFromASuccessfulResult() // AC-32
    {
        var envelope = T4ProcessingFixture.Envelope();
        var payload = envelope.T4Payload!;
        var validated = DocumentExtraction.Services.ExtractionEnvelopeValidator.Validate(envelope).Value!;

        var result = new T4CaseProcessor(T4ProcessingFixture.Embedded).Process(validated);
        Assert.True(result.IsSuccess);

        var reachable = ReachableObjects(result.Value!).ToArray();

        // Reachability guard: the walk must actually be finding things, or "no
        // T4RawInput found" would be a statement about a broken traversal.
        Assert.True(reachable.Length > 5, $"The object walk found only {reachable.Length} objects.");
        Assert.Contains(reachable, o => ReferenceEquals(o, result.Value!.Slip));

        Assert.DoesNotContain(reachable, o => o is T4RawInput);
        Assert.DoesNotContain(reachable, o => o is T4RawBox);
        Assert.DoesNotContain(reachable, o => ReferenceEquals(o, payload));

        // The caller still holds it — omission from the result is not deletion.
        Assert.Same(payload, validated.T4Payload);
    }

    // ============================== AC-33 — closed field set on our own errors

    [Fact]
    public void SelfOriginatedErrors_UseTheClosedFieldSetAndEchoNoCallerValue() // AC-30, AC-33
    {
        var declared = typeof(T4ProcessingFields)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && !field.IsInitOnly)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declared);

        // The pipelines whose rejection this module originates itself. Engine
        // errors keep the engine's own field names and are surfaced verbatim, so
        // the closed set governs only these.
        var selfOriginated = new[] { "GenericEvidenceKind", "NullBoxes", "NullBoxElement", "BlankBoxCode", "UnsupportedYear" };

        var emitted = selfOriginated
            .SelectMany(pipeline => T4ProcessingFixture.Run(pipeline, "DocumentId", "ZZMARKERZZ").Result.Errors)
            .ToArray();

        Assert.NotEmpty(emitted);

        foreach (var error in emitted)
        {
            Assert.Contains(error.Field, declared);
            Assert.DoesNotContain("ZZMARKERZZ", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("doc-1", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("firm-1", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("blob://", error.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every object reachable from <paramref name="root"/> through public
    /// properties and enumerables, so "the raw payload is unreachable" is a claim
    /// about the whole graph rather than the first level of it.
    /// </summary>
    private static IEnumerable<object> ReachableObjects(object root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<object>();
        queue.Enqueue(root);
        seen.Add(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            yield return current;

            if (current is string)
            {
                continue;
            }

            if (current is System.Collections.IEnumerable items)
            {
                foreach (var item in items)
                {
                    if (item is not null && seen.Add(item))
                    {
                        queue.Enqueue(item);
                    }
                }

                continue;
            }

            var properties = current.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetIndexParameters().Length == 0 && !property.PropertyType.IsPrimitive);

            foreach (var property in properties)
            {
                object? child;
                try
                {
                    child = property.GetValue(current);
                }
                catch (TargetInvocationException)
                {
                    continue;
                }

                if (child is not null && child is not string && seen.Add(child))
                {
                    queue.Enqueue(child);
                }
            }
        }
    }
}
