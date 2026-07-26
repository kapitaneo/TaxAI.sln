using TaxLogicEngine.Models;

namespace CaseProcessing.Services;

/// <summary>
/// Pipeline stage 4: proves the payload is structurally traversable before the
/// parser touches it. <c>T4SlipParser.Parse</c> groups <c>Boxes</c> with no null
/// guard, so a payload that cleared spec 007's envelope gate can still raise a
/// <c>NullReferenceException</c> there — and an escaping exception is not
/// fail-closed behaviour. This guard closes that gap at the seam, leaving spec
/// 003's shipped parser untouched.
/// <para>
/// It returns one <see cref="ValidationError"/> per structural defect, in fixed
/// order, and <b>never throws</b>, whatever the input. It reads no
/// <c>T4RawBox.Value</c>, makes no judgement about whether a code is known,
/// required, or meaningful, and never trims, normalizes, deduplicates, reorders,
/// or drops a box — duplicates must still reach the parser so the parser's own
/// duplicate rejection fires.
/// </para>
/// <para>
/// <b>Internal</b> because it is a seam guard, not public API: the module exposes
/// exactly one behaviour, <c>Process</c>. The test project sees it through
/// <c>InternalsVisibleTo</c>.
/// </para>
/// <para>
/// Wave 0 contract stage: the signature is declared; the implementation lands in
/// Wave 1 (task T02).
/// </para>
/// </summary>
internal static class PayloadStructureGuard
{
    /// <summary>
    /// Returns every structural defect in <paramref name="payload"/>, in fixed
    /// order, or an empty list when the payload is traversable.
    /// </summary>
    internal static IReadOnlyList<ValidationError> Inspect(T4RawInput payload) =>
        throw new NotImplementedException("PayloadStructureGuard.Inspect is implemented by spec 008 task T02.");
}
