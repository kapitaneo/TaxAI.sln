using System.Reflection;
using System.Text.RegularExpressions;
using CaseProcessing.Models;
using CaseProcessing.Services;
using TaxLogicEngine.Models;

namespace CaseProcessing.Tests.Services;

/// <summary>
/// T02 — the stage-4 structural seam guard, at unit level. Covers AC-11 (null
/// <c>Boxes</c>), AC-12 (a null element) and AC-13 (a null, empty, or
/// whitespace-only box code), plus the constraints that make this guard safe to
/// sit in front of spec 003's shipped parser: it never throws, reads no
/// <c>T4RawBox.Value</c>, judges no box code, and leaves the payload untouched
/// and reference-identical.
/// </summary>
public sealed class PayloadStructureGuardTests
{
    /// <summary>
    /// The plants used wherever this suite proves a payload value is never read.
    /// The two non-numeric markers matter: a nine-digit regex alone cannot catch
    /// an echoed opaque string.
    /// </summary>
    private static readonly string[] Plants =
    {
        "123456789",
        "123-456-789",
        "123 456 789",
        "ZZMARKERZZ",
        "<<leak-me>>",
    };

    private static readonly Regex NineConsecutiveDigits = new(@"\d{9}", RegexOptions.Compiled);

    // ---------------------------------------------------------------- positive

    [Fact]
    public void Inspect_WellFormedPayload_YieldsNoErrors()
    {
        var payload = PayloadWith(
            new T4RawBox("14", "52000.00"),
            new T4RawBox("12", "046454286"),
            new T4RawBox("16", "3500.00"),
            new T4RawBox("54", "123456789RP0001"));

        Assert.Empty(PayloadStructureGuard.Inspect(payload));
    }

    [Fact]
    public void Inspect_EmptyBoxCollection_YieldsNoErrors()
    {
        // Structurally traversable. Whether an empty slip is parseable is the
        // parser's judgement, not this guard's.
        Assert.Empty(PayloadStructureGuard.Inspect(PayloadWith()));
    }

    [Fact]
    public void Inspect_DuplicateCodes_YieldsNoErrors()
    {
        // Spec 007 preserves duplicates by design so the parser's own duplicate
        // rejection can fire. Rejecting or collapsing them here would silence it.
        var payload = PayloadWith(
            new T4RawBox("14", "52000.00"),
            new T4RawBox("14", "9000.00"),
            new T4RawBox("14", "0.00"));

        Assert.Empty(PayloadStructureGuard.Inspect(payload));
    }

    [Fact]
    public void Inspect_UnknownBlankValuedAndGarbageBoxes_YieldNoErrors()
    {
        // Every defect below is a *content* defect: an unknown code, an absent
        // value, a blank value, a non-numeric amount, a negative amount, an
        // untrimmed code. The guard is structural only, so all of them pass
        // through to the parser, which is where that judgement belongs.
        var payload = PayloadWith(
            new T4RawBox("ZZZ", "anything"),
            new T4RawBox("14", null),
            new T4RawBox("16", ""),
            new T4RawBox("18", "   "),
            new T4RawBox("22", "not-a-number"),
            new T4RawBox("24", "-1"),
            new T4RawBox("85", "\u0000not-an-amount"),
            new T4RawBox("  14  ", "52000.00"));

        Assert.Empty(PayloadStructureGuard.Inspect(payload));
    }

    // ---------------------------------------------------------------- negative

    [Fact]
    public void Inspect_NullBoxCollection_YieldsOnePayloadBoxesError()
    {
        // AC-11. T4SlipParser.Parse reaches input.Boxes.GroupBy(...) with no null
        // guard, so without this rule the pipeline throws instead of failing closed.
        var payload = new T4RawInput { Year = 2025, EmployerName = "Acme Ltd", Boxes = null! };

        var errors = PayloadStructureGuard.Inspect(payload);

        var error = Assert.Single(errors);
        Assert.Equal(T4ProcessingFields.PayloadBoxes, error.Field);
    }

    [Fact]
    public void Inspect_NullPayload_YieldsOnePayloadBoxesErrorAndDoesNotThrow()
    {
        // Unreachable through the pipeline — stage 2 rejects an absent payload
        // first — but a direct caller must not be able to make the guard throw.
        var errors = PayloadStructureGuard.Inspect(null!);

        var error = Assert.Single(errors);
        Assert.Equal(T4ProcessingFields.PayloadBoxes, error.Field);
    }

    [Fact]
    public void Inspect_NullElementInBoxes_YieldsOnePayloadBoxesError()
    {
        // AC-12.
        var payload = PayloadWith(new T4RawBox("14", "52000.00"), null!, new T4RawBox("16", "3500.00"));

        var errors = PayloadStructureGuard.Inspect(payload);

        var error = Assert.Single(errors);
        Assert.Equal(T4ProcessingFields.PayloadBoxes, error.Field);
    }

    [Fact]
    public void Inspect_EveryElementNull_YieldsOneErrorPerElement()
    {
        var payload = PayloadWith(null!, null!, null!);

        var errors = PayloadStructureGuard.Inspect(payload);

        Assert.Equal(3, errors.Count);
        Assert.All(errors, error => Assert.Equal(T4ProcessingFields.PayloadBoxes, error.Field));
    }

    [Fact]
    public void Inspect_NullBoxCode_YieldsOnePayloadBoxCodeError()
    {
        // AC-13, first arm.
        var payload = PayloadWith(new T4RawBox("14", "52000.00"), new T4RawBox(null!, "3500.00"));

        var errors = PayloadStructureGuard.Inspect(payload);

        var error = Assert.Single(errors);
        Assert.Equal(T4ProcessingFields.PayloadBoxCode, error.Field);
    }

    [Fact]
    public void Inspect_EmptyBoxCode_YieldsOnePayloadBoxCodeError()
    {
        // AC-13, second arm.
        var payload = PayloadWith(new T4RawBox("14", "52000.00"), new T4RawBox("", "3500.00"));

        var errors = PayloadStructureGuard.Inspect(payload);

        var error = Assert.Single(errors);
        Assert.Equal(T4ProcessingFields.PayloadBoxCode, error.Field);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    [InlineData("\u00A0")]
    public void Inspect_WhitespaceOnlyBoxCode_YieldsOnePayloadBoxCodeError(string code)
    {
        // AC-13, third arm. Null, empty, and whitespace-only are all blank.
        var payload = PayloadWith(new T4RawBox("14", "52000.00"), new T4RawBox(code, "3500.00"));

        var errors = PayloadStructureGuard.Inspect(payload);

        var error = Assert.Single(errors);
        Assert.Equal(T4ProcessingFields.PayloadBoxCode, error.Field);
    }

    [Fact]
    public void Inspect_SeveralDefectsAtOnce_YieldsOneErrorEachInFixedOrder()
    {
        // Rule-major, then positional: every null element first, then every blank
        // code. One error per defect — nothing is collapsed or short-circuited
        // once the collection itself is traversable.
        var payload = PayloadWith(
            null!,
            new T4RawBox(null!, "a"),
            new T4RawBox("14", "52000.00"),
            null!,
            new T4RawBox("   ", "b"),
            new T4RawBox("", "c"));

        var errors = PayloadStructureGuard.Inspect(payload);

        Assert.Equal(
            new[]
            {
                T4ProcessingFields.PayloadBoxes,
                T4ProcessingFields.PayloadBoxes,
                T4ProcessingFields.PayloadBoxCode,
                T4ProcessingFields.PayloadBoxCode,
                T4ProcessingFields.PayloadBoxCode,
            },
            errors.Select(error => error.Field).ToArray());
    }

    [Fact]
    public void Inspect_NullBoxCollection_ShortCircuitsAndNeverReportsPerBoxRules()
    {
        // There is no collection to walk, so exactly one error is possible.
        var payload = new T4RawInput { Year = 2025, EmployeeName = "Jane Doe", Boxes = null! };

        Assert.Single(PayloadStructureGuard.Inspect(payload));
    }

    // ------------------------------------------------------------- never throws

    [Fact]
    public void Inspect_AnyPayloadShape_NeverThrows()
    {
        foreach (var payload in HostilePayloads())
        {
            var exception = Record.Exception(() => PayloadStructureGuard.Inspect(payload));
            Assert.Null(exception);
        }
    }

    [Fact]
    public void Inspect_AnyPayloadShape_ReturnsANonNullList()
    {
        foreach (var payload in HostilePayloads())
        {
            Assert.NotNull(PayloadStructureGuard.Inspect(payload));
        }
    }

    // ----------------------------------------------------------- closed field set

    [Fact]
    public void Inspect_EveryEmittedField_IsAT4ProcessingFieldsConstant()
    {
        var declared = typeof(T4ProcessingFields)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && !field.IsInitOnly)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declared);

        var emitted = HostilePayloads()
            .SelectMany(PayloadStructureGuard.Inspect)
            .Select(error => error.Field)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(emitted);
        Assert.All(emitted, field => Assert.Contains(field, declared));
    }

    [Fact]
    public void Inspect_EmitsOnlyTheTwoStructuralFields()
    {
        var emitted = HostilePayloads()
            .SelectMany(PayloadStructureGuard.Inspect)
            .Select(error => error.Field)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(field => field, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { T4ProcessingFields.PayloadBoxCode, T4ProcessingFields.PayloadBoxes },
            emitted);
    }

    // -------------------------------------------------------- reads no box value

    [Fact]
    public void Inspect_PlantedValuesEverywhere_AppearInNoErrorFieldOrMessage()
    {
        // Every value is a planted SIN or marker, and the payload is defective in
        // both structural ways, so errors are certainly produced.
        var errors = PayloadStructureGuard.Inspect(PayloadWithPlantedValues());

        Assert.NotEmpty(errors);
        foreach (var error in errors)
        {
            var rendered = error.Field + " " + error.Message + " " + error.ToString();

            foreach (var plant in Plants)
            {
                Assert.DoesNotContain(plant, rendered, StringComparison.Ordinal);
            }

            // Omission, not masking: not even a partial form like ***-**-789.
            Assert.DoesNotMatch(NineConsecutiveDigits, rendered);
            Assert.DoesNotContain("789", rendered, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Inspect_ValuesDoNotAffectTheResult()
    {
        // Same structure, wildly different values: identical errors. A guard that
        // read a value could not satisfy this.
        var structural = PayloadWith(
            new T4RawBox("14", "52000.00"),
            null!,
            new T4RawBox(null!, "3500.00"),
            new T4RawBox("  ", "0.00"));

        var planted = PayloadWith(
            new T4RawBox("14", Plants[0]),
            null!,
            new T4RawBox(null!, null),
            new T4RawBox("  ", Plants[4]));

        Assert.Equal(
            PayloadStructureGuard.Inspect(structural).ToArray(),
            PayloadStructureGuard.Inspect(planted));
    }

    [Fact]
    public void Inspect_MessagesAreNonInterpolatedConstants()
    {
        // Six defective boxes, each carrying a different value and a different
        // blank code, must still produce exactly one distinct message per rule. A
        // template that varies with its input is an interpolated one.
        var payload = PayloadWith(
            null!,
            null!,
            new T4RawBox(null!, Plants[0]),
            new T4RawBox("", Plants[1]),
            new T4RawBox(" ", Plants[2]),
            new T4RawBox("\t", Plants[3]));

        var distinctMessagesByField = PayloadStructureGuard.Inspect(payload)
            .GroupBy(error => error.Field, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Message).Distinct(StringComparer.Ordinal).Count(),
                StringComparer.Ordinal);

        Assert.Equal(1, distinctMessagesByField[T4ProcessingFields.PayloadBoxes]);
        Assert.Equal(1, distinctMessagesByField[T4ProcessingFields.PayloadBoxCode]);
    }

    [Fact]
    public void GuardSource_ReadsNoBoxValue()
    {
        // The behavioural arms above prove no value *changes* the result; this
        // proves no value is even touched. Executable lines only — the file's
        // documentation legitimately names T4RawBox.Value while explaining why it
        // is never read.
        var offending = ExecutableGuardSourceLines()
            .Where(line => Regex.IsMatch(line, @"\.Value\b"))
            .ToArray();

        Assert.Empty(offending);
    }

    [Fact]
    public void GuardSourceScanner_SeesTheRealFile()
    {
        // Scanner guard: without this, a wrong path or an over-eager comment
        // filter would make the scan above pass for free.
        var lines = ExecutableGuardSourceLines();

        Assert.True(lines.Length > 15, "The guard source scan found implausibly few executable lines.");
        Assert.Contains(lines, line => line.Contains("PayloadStructureGuard", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("IsNullOrWhiteSpace", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains(nameof(T4ProcessingFields.PayloadBoxCode), StringComparison.Ordinal));
    }

    // ------------------------------------------------- payload left untouched

    [Fact]
    public void Inspect_LeavesThePayloadUnmutatedAndReferenceIdentical()
    {
        var boxes = new List<T4RawBox>
        {
            new("14", "52000.00"),
            new("14", "9000.00"),
            null!,
            new("  16  ", "3500.00"),
            new("", "0.00"),
        };

        var payload = new T4RawInput
        {
            Year = 2025,
            EmployerName = "Acme Ltd",
            EmployerAddress = "1 Main St",
            EmployeeName = "Jane Doe",
            EmployeeAddress = "2 Side Rd",
            ExemptCppQpp = true,
            Boxes = boxes,
        };

        var snapshot = boxes.ToArray();

        PayloadStructureGuard.Inspect(payload);

        Assert.Same(boxes, payload.Boxes);
        Assert.Equal(snapshot.Length, payload.Boxes.Count);
        for (var index = 0; index < snapshot.Length; index++)
        {
            // Reference-identical elements: nothing was trimmed, normalized,
            // deduplicated, reordered, dropped, or replaced.
            Assert.Same(snapshot[index], payload.Boxes[index]);
        }

        Assert.Equal("  16  ", payload.Boxes[3].Code);
        Assert.Equal(2025, payload.Year);
        Assert.Equal("Acme Ltd", payload.EmployerName);
        Assert.Equal("1 Main St", payload.EmployerAddress);
        Assert.Equal("Jane Doe", payload.EmployeeName);
        Assert.Equal("2 Side Rd", payload.EmployeeAddress);
        Assert.True(payload.ExemptCppQpp);
    }

    [Fact]
    public void Inspect_IsDeterministicAcrossRepeatedCalls()
    {
        var payload = PayloadWithPlantedValues();

        var first = PayloadStructureGuard.Inspect(payload).ToArray();

        for (var replay = 0; replay < 5; replay++)
        {
            // Equal sequences, not merely equal sets: ordering is part of the
            // contract.
            Assert.Equal(first, PayloadStructureGuard.Inspect(payload));
        }
    }

    // ------------------------------------------------------------------ helpers

    private static T4RawInput PayloadWith(params T4RawBox[] boxes) =>
        new()
        {
            Year = 2025,
            EmployerName = "Acme Ltd",
            EmployeeName = "Jane Doe",
            Boxes = boxes,
        };

    private static T4RawInput PayloadWithPlantedValues() =>
        new()
        {
            Year = 2025,
            EmployerName = Plants[3],
            EmployerAddress = Plants[4],
            EmployeeName = Plants[0],
            EmployeeAddress = Plants[1],
            Boxes = new T4RawBox[]
            {
                new("12", Plants[0]),
                new("14", Plants[1]),
                null!,
                new(null!, Plants[2]),
                new("  ", Plants[3]),
                new("", Plants[4]),
            },
        };

    /// <summary>
    /// Every payload shape this suite throws at the guard, so the never-throws,
    /// closed-field-set, and non-null-result assertions cover one corpus rather
    /// than drifting apart.
    /// </summary>
    private static IEnumerable<T4RawInput> HostilePayloads()
    {
        yield return null!;
        yield return new T4RawInput();
        yield return new T4RawInput { Boxes = null! };
        yield return new T4RawInput { Year = null, Boxes = Array.Empty<T4RawBox>() };
        yield return PayloadWith();
        yield return PayloadWith(new T4RawBox("14", "52000.00"));
        yield return PayloadWith(null!);
        yield return PayloadWith(null!, null!);
        yield return PayloadWith(new T4RawBox(null!, null));
        yield return PayloadWith(new T4RawBox("", ""));
        yield return PayloadWith(new T4RawBox("   ", "   "));
        yield return PayloadWith(new T4RawBox("\t\r\n", "x"));
        yield return PayloadWith(new T4RawBox("14", null), new T4RawBox("14", null));
        yield return PayloadWithPlantedValues();
        yield return new T4RawInput { Year = 2025, Boxes = new List<T4RawBox> { null! } };
    }

    private static string[] ExecutableGuardSourceLines()
    {
        var path = Path.Combine(
            FindRepositoryRoot(), "src", "CaseProcessing", "Services", "PayloadStructureGuard.cs");

        Assert.True(File.Exists(path), $"Guard source not found at {path}.");

        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("///", StringComparison.Ordinal) &&
                !line.StartsWith("//", StringComparison.Ordinal) &&
                !line.StartsWith("*", StringComparison.Ordinal) &&
                !line.StartsWith("/*", StringComparison.Ordinal))
            .ToArray();
    }

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
