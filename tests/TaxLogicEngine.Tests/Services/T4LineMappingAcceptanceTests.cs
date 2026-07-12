using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.RegularExpressions;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

/// <summary>
/// End-to-end acceptance suite for T4 box → CRA line mapping (spec 004, T07): the
/// <b>real</b> <see cref="EmbeddedT4LineMapProvider"/> wired into
/// <see cref="T4LineMapper"/> against the shipped 2025 config, covering the PRD
/// acceptance criteria not already unit-covered — AC2 (full slip → all verified
/// lines), AC20 (no full SIN in any output), AC21 (2025 config never applied to
/// another year), AC22 (no CRA line-number literal in the compiled T4 mapping
/// types), and AC23 (determinism). The expected line numbers here are the test's own
/// assertion of the CRA fact; production code holds none (that is AC22).
/// </summary>
public class T4LineMappingAcceptanceTests
{
    private const string Sin = "123456789";
    private static readonly T4LineMapper Mapper = new(new EmbeddedT4LineMapProvider());

    // The CRA-verified 2025 lines (Source I). Asserted here as the expected mapping
    // outcome; AC22 proves none of these is a literal in the production types.
    private static readonly string[] CraLines2025 =
    {
        "10100", "31200", "20700", "43700", "21200", "20600",
        "24900", "10120", "24400", "22900",
    };

    private static TaxLineEntry Line(T4MappingResult r, string line) =>
        r.Lines.Single(e => e.LineNumber == line);

    private static T4Slip Slip(int year = 2025, Action<T4SlipBuilder>? configure = null)
    {
        var b = new T4SlipBuilder();
        configure?.Invoke(b);
        return b.Build(year);
    }

    // ---- AC2: full valid slip maps each direct box to its verified line --------

    [Fact]
    public void AC2_FullValidSlip_MapsEachDirectBoxToItsVerifiedLine()
    {
        var r = Mapper.Map(new[]
        {
            Slip(configure: b =>
            {
                b.EmploymentIncome = 50000.00m;   // Box 14 → 10100
                b.EiPremiums = 800.00m;           // Box 18 → 31200
                b.RppContributions = 1200.00m;    // Box 20 → 20700
                b.IncomeTaxDeducted = 9000.00m;   // Box 22 → 43700
                b.UnionDues = 700.00m;            // Box 44 → 21200
                b.PensionAdjustment = 2000.00m;   // Box 52 → 20600
                b.Other = new[]
                {
                    new T4OtherInformation("39", 100.00m), // → 24900
                    new T4OtherInformation("41", 50.00m),  // → 24900 (summed)
                    new T4OtherInformation("42", 300.00m), // → 10120
                    new T4OtherInformation("43", 400.00m), // → 24400
                    new T4OtherInformation("77", 250.00m), // → 22900
                };
            }),
        });

        Assert.True(r.IsSuccess);
        var v = r.Value!;

        Assert.Equal(50000.00m, Line(v, "10100").Amount);
        Assert.Equal(800.00m, Line(v, "31200").Amount);
        Assert.Equal(1200.00m, Line(v, "20700").Amount);
        Assert.Equal(9000.00m, Line(v, "43700").Amount);
        Assert.Equal(700.00m, Line(v, "21200").Amount);
        Assert.Equal(2000.00m, Line(v, "20600").Amount);
        Assert.Equal(150.00m, Line(v, "24900").Amount); // code 39 + code 41
        Assert.Equal(300.00m, Line(v, "10120").Amount);
        Assert.Equal(400.00m, Line(v, "24400").Amount);
        Assert.Equal(250.00m, Line(v, "22900").Amount);

        // Exactly the ten verified lines, nothing extra, nothing unmapped.
        Assert.Equal(CraLines2025.OrderBy(x => x, StringComparer.Ordinal),
            v.Lines.Select(e => e.LineNumber));
        Assert.Empty(v.UnmappedBoxCodes);

        // Code 39 + 41 both recorded as provenance on the shared line.
        var line24900 = Line(v, "24900");
        Assert.Contains("39", line24900.SourceBoxCodes);
        Assert.Contains("41", line24900.SourceBoxCodes);
    }

    // ---- AC20: no full SIN anywhere in mapping output --------------------------

    [Fact]
    public void AC20_NoFullSin_InAnySerializedOutput()
    {
        // A slip whose Box 12 SIN is 123456789, plus a deferred box (16) that lands in
        // UnmappedBoxCodes, so every output surface carries real content to inspect.
        var r = Mapper.Map(new[]
        {
            Slip(configure: b =>
            {
                b.EmploymentIncome = 50000.00m;
                b.CppContributions = 3500.00m; // Box 16 → unmapped
            }),
        });

        Assert.True(r.IsSuccess);
        var serialized = JsonSerializer.Serialize(r.Value!);

        Assert.DoesNotContain(Sin, serialized);
        // No 9-consecutive-digit SIN substring anywhere in the mapped result.
        Assert.DoesNotMatch(new Regex(@"\d{9}"), serialized);

        // Belt and suspenders: the SIN never reached the amount pipeline as provenance.
        Assert.All(r.Value!.Lines, e => Assert.All(e.SourceBoxCodes, c => Assert.DoesNotContain(Sin, c)));
        Assert.All(r.Value!.UnmappedBoxCodes, c => Assert.DoesNotContain(Sin, c));
    }

    // ---- AC21: 2025 config is never applied to another year --------------------

    [Fact]
    public void AC21_2025ConfigNotAppliedToAnotherYear_FailsClosed()
    {
        // 2024 has no embedded config; the mapper must fail closed rather than reuse
        // the 2025 config. A SIN-carrying slip also guards that errors leak no PII.
        var r = Mapper.Map(new[] { Slip(2024, b => b.EmploymentIncome = 50000.00m) });

        Assert.True(r.IsFailure);
        Assert.Null(r.Value);
        Assert.Contains(r.Errors, e => e.Field == "TaxYear");
        Assert.All(r.Errors, e =>
        {
            Assert.DoesNotContain(Sin, e.Message);
            Assert.DoesNotMatch(new Regex(@"\d{9}"), e.Message);
        });
    }

    // ---- AC22: no CRA line-number literal in the compiled T4 mapping types -----

    [Fact]
    public void AC22_NoCraLineNumberLiteral_InCompiledT4MappingTypes()
    {
        var mappingTypes = new[]
        {
            typeof(T4MappingResult),
            typeof(T4BoxLineMapping),
            typeof(T4LineMappingConfig),
            typeof(T4LineMap),
            typeof(T4OtherInformationCodes),
            typeof(IT4LineMapProvider),
            typeof(EmbeddedT4LineMapProvider),
            typeof(T4SlipBoxAmounts),
            typeof(T4LineMapper),
        };

        var literals = mappingTypes
            .SelectMany(WithNestedTypes)
            .SelectMany(StringLiteralsOf)
            .ToList();

        // Guard against a vacuous pass: the scan must actually be reading emitted
        // literals (error messages, the resource name, box codes) — otherwise
        // "contains no line number" would be trivially true.
        Assert.NotEmpty(literals);

        foreach (var line in CraLines2025)
        {
            Assert.DoesNotContain(line, literals);
        }
    }

    // ---- AC23: determinism -----------------------------------------------------

    [Fact]
    public void AC23_IdenticalInput_ProducesEqualResults()
    {
        T4Slip[] Build() => new[]
        {
            Slip(configure: b =>
            {
                b.EmploymentIncome = 50000.00m;
                b.IncomeTaxDeducted = 9000.00m;
                b.CppContributions = 3500.00m; // unmapped
                b.Other = new[] { new T4OtherInformation("39", 100.00m), new T4OtherInformation("41", 50.00m) };
            }),
        };

        var a = Mapper.Map(Build()).Value!;
        var b = Mapper.Map(Build()).Value!;

        Assert.Equal(
            a.Lines.Select(e => (e.LineNumber, e.Amount, string.Join(",", e.SourceBoxCodes))),
            b.Lines.Select(e => (e.LineNumber, e.Amount, string.Join(",", e.SourceBoxCodes))));
        Assert.Equal(a.UnmappedBoxCodes, b.UnmappedBoxCodes);
    }

    // ---- reflection helpers (AC22) --------------------------------------------

    private static IEnumerable<Type> WithNestedTypes(Type type)
    {
        yield return type;
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (var t in WithNestedTypes(nested))
            {
                yield return t;
            }
        }
    }

    // Every string literal a type emits: const string fields plus every ldstr in
    // every method/ctor body (including compiler-generated accessors). If a CRA line
    // number were hardcoded in C#, it would appear here.
    private static IEnumerable<string> StringLiteralsOf(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(flags))
        {
            if (field.IsLiteral && field.FieldType == typeof(string) &&
                field.GetRawConstantValue() is string constValue)
            {
                yield return constValue;
            }
        }

        var methods = type.GetMethods(flags)
            .Cast<MethodBase>()
            .Concat(type.GetConstructors(flags));

        foreach (var method in methods)
        {
            foreach (var literal in LdstrLiterals(method))
            {
                yield return literal;
            }
        }
    }

    private static IEnumerable<string> LdstrLiterals(MethodBase method)
    {
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception)
        {
            yield break; // abstract / extern / no managed body
        }

        if (il is null)
        {
            yield break;
        }

        var module = method.Module;
        var pos = 0;
        while (pos < il.Length)
        {
            short key;
            if (il[pos] == 0xFE && pos + 1 < il.Length)
            {
                key = unchecked((short)(0xFE00 | il[pos + 1]));
                pos += 2;
            }
            else
            {
                key = il[pos];
                pos += 1;
            }

            if (!OpCodesByValue.TryGetValue(key, out var op))
            {
                yield break; // unrecognized opcode: stop walking this method safely
            }

            if (op == OpCodes.Ldstr)
            {
                var token = BitConverter.ToInt32(il, pos);
                string? resolved = null;
                try
                {
                    resolved = module.ResolveString(token);
                }
                catch (Exception)
                {
                    // not a user-string token; ignore
                }

                if (resolved is not null)
                {
                    yield return resolved;
                }
            }

            pos += OperandSize(op, il, pos);
        }
    }

    private static int OperandSize(OpCode op, byte[] il, int operandPos) => op.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, operandPos),
        _ => 4, // all remaining inline operands (tokens, branch targets, i4, r4) are 4 bytes
    };

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue = BuildOpCodeMap();

    private static IReadOnlyDictionary<short, OpCode> BuildOpCodeMap()
    {
        var map = new Dictionary<short, OpCode>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode op)
            {
                map[op.Value] = op;
            }
        }

        return map;
    }

    private sealed class T4SlipBuilder
    {
        public decimal EmploymentIncome, EiPremiums, RppContributions, IncomeTaxDeducted,
            UnionDues, PensionAdjustment, CppContributions;
        public IReadOnlyList<T4OtherInformation> Other = Array.Empty<T4OtherInformation>();

        public T4Slip Build(int year) => new()
        {
            Year = year,
            EmployerName = "ACME CORP",
            EmployeeName = "DOE, JANE",
            DentalBenefitCode = "1",
            Sin = new RecipientIdentifier(Sin),
            EmploymentIncome = EmploymentIncome,
            EiPremiums = EiPremiums,
            RppContributions = RppContributions,
            IncomeTaxDeducted = IncomeTaxDeducted,
            UnionDues = UnionDues,
            PensionAdjustment = PensionAdjustment,
            CppContributions = CppContributions,
            OtherInformation = Other,
        };
    }
}
