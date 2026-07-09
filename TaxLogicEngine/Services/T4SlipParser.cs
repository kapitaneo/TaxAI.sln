using TaxLogicEngine.Models;

namespace TaxLogicEngine.Services;

/// <summary>
/// Turns already-extracted <see cref="T4RawInput"/> into a typed <see cref="T4Slip"/>.
/// Pure and deterministic: no I/O, no ambient state, same input → same result.
/// <para>
/// Fail-closed: input is validated first; on any problem the parser returns
/// <see cref="ParseResult{T}.Failure"/> with structured errors and <b>no</b> model,
/// so partially-valid data can never leak downstream. Validation messages never
/// echo the Box 12 SIN.
/// </para>
/// <para>
/// Wave 0 contract stage: the signature is fixed here; the implementation lands in
/// a later task, so <see cref="Parse"/> currently throws
/// <see cref="NotImplementedException"/>.
/// </para>
/// </summary>
public sealed class T4SlipParser
{
    /// <summary>
    /// Validates and parses one slip's extracted boxes. Returns the typed slip on
    /// success, or all collected validation errors (and no model) on failure.
    /// </summary>
    public ParseResult<T4Slip> Parse(T4RawInput input) => throw new NotImplementedException();
}
