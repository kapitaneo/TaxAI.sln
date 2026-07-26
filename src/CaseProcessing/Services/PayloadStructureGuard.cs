using CaseProcessing.Models;
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
/// </summary>
internal static class PayloadStructureGuard
{
    // Fixed message templates. Deliberately constants with no interpolation: a
    // template that cannot take an argument cannot leak a caller or payload
    // value. No box code, no box value, and no index is ever named — a defect is
    // reported by rule, not by content (PII by omission).
    private const string BoxesRequired = "The T4 payload's box collection is required.";
    private const string BoxRequired = "A T4 payload box entry must not be null.";
    private const string BoxCodeRequired = "A T4 payload box code is required and must be non-blank.";

    /// <summary>
    /// Returns every structural defect in <paramref name="payload"/>, in fixed
    /// order, or an empty list when the payload is traversable.
    /// <para>
    /// Order is <b>rule-major, then positional</b>: an untraversable collection
    /// short-circuits; otherwise every null-element defect is reported in index
    /// order, then every blank-code defect in index order. Rule-major order
    /// matches the fixed rule order the architecture declares and keeps the
    /// sequence identical for a given payload, which is the determinism the
    /// contract requires. [ASSUMED] — architecture.md fixes the rule order but
    /// not whether the two per-box rules interleave positionally; one-pass human
    /// override.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<ValidationError> Inspect(T4RawInput payload)
    {
        // Rule 1 — the collection itself must be traversable, and nothing below
        // can be evaluated without it. A null payload is reported here rather
        // than thrown on: stage 2 already rejects an absent payload, so this arm
        // is unreachable through the pipeline, but "never throws, whatever the
        // input" must hold for a direct caller too. Either way there is no box
        // collection to walk, which is exactly what PayloadBoxes names.
        var boxes = payload?.Boxes;
        if (boxes is null)
        {
            return new[] { new ValidationError(T4ProcessingFields.PayloadBoxes, BoxesRequired) };
        }

        var errors = new List<ValidationError>();

        // Rule 2 — no null element. Indexed, never enumerated into a new
        // collection, so the payload is read exactly as supplied: nothing is
        // trimmed, normalized, deduplicated, reordered, dropped, or mutated.
        for (var index = 0; index < boxes.Count; index++)
        {
            if (boxes[index] is null)
            {
                errors.Add(new ValidationError(T4ProcessingFields.PayloadBoxes, BoxRequired));
            }
        }

        // Rule 3 — every surviving box carries a usable code. Null, empty, and
        // whitespace-only are all blank. Nothing here decides whether a code is
        // known, required, or meaningful, and no T4RawBox.Value is read — typing
        // and validating values remains T4SlipParser's job alone, so a duplicate,
        // unknown, or garbage-valued box passes this guard untouched.
        for (var index = 0; index < boxes.Count; index++)
        {
            var box = boxes[index];
            if (box is not null && string.IsNullOrWhiteSpace(box.Code))
            {
                errors.Add(new ValidationError(T4ProcessingFields.PayloadBoxCode, BoxCodeRequired));
            }
        }

        return errors;
    }
}
