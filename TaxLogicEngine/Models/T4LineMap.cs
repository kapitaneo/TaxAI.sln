namespace TaxLogicEngine.Models;

/// <summary>
/// The resolved, validated box→line lookup for a single tax year, built from a
/// <see cref="T4LineMappingConfig"/>. Construction is fail-closed: a config with a
/// duplicate box code, an empty line list, a blank code/line, or an <b>unknown box
/// code</b> (not a standard <see cref="T4BoxCodes"/> box nor a
/// <see cref="T4OtherInformationCodes"/> code) yields
/// <see cref="ParseResult{T}.Failure"/> rather than a half-built map (FR6–FR8).
/// Mirrors <c>T5LineMap</c>, plus the unknown-code check.
/// <para>
/// Wave 0 contract stage: members are declared but not implemented.
/// </para>
/// </summary>
public sealed class T4LineMap
{
    /// <summary>Builds a validated map from config, or returns the config errors.</summary>
    public static ParseResult<T4LineMap> Create(T4LineMappingConfig config) =>
        throw new NotImplementedException();

    /// <summary>The tax year this map applies to.</summary>
    public int TaxYear => throw new NotImplementedException();

    /// <summary>The box codes this map knows about.</summary>
    public IReadOnlyCollection<string> BoxCodes => throw new NotImplementedException();

    /// <summary>The CRA lines a box maps to, or null when the box has no mapping.</summary>
    public IReadOnlyList<string>? LinesFor(string boxCode) => throw new NotImplementedException();
}
