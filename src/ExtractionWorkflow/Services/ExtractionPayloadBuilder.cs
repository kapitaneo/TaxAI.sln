using ExtractionWorkflow.Models;
using TaxLogicEngine.Models;

namespace ExtractionWorkflow.Services;

/// <summary>
/// Assembles the engine's raw T4 payload from untrusted provider output, in
/// memory, for the duration of one call.
/// <para>
/// This is the single assembly path. The payload it builds is the only place
/// Box 12 exists: it is passed to a caller-supplied continuation and never
/// returned, so no caller can hold it, store it, or hand it to something that
/// serializes it. When the continuation returns, the payload is unreachable.
/// That is why the raw payload is never persisted and why a correction re-reads
/// the source and rebuilds it instead — a stored payload would be a second
/// unmasked SIN carrier, and an encrypted column is still a column.
/// </para>
/// <para>
/// Nothing here decides anything about tax. Values are copied across as
/// strings, in the order the provider produced them, including duplicate box
/// codes: collapsing, deduplicating, reordering, or dropping a box would hide
/// the very thing the engine's own duplicate-box rejection exists to catch. No
/// box code is looked up, defaulted, required, or given a meaning, and no value
/// is invented for an absent field — a missing Box 54 stays missing and a
/// missing address stays missing.
/// </para>
/// <para>
/// The payload's year is what the document showed. The year an extraction is
/// attributed to comes from the server-stored intake case, and the existing
/// envelope validator refuses the pair when the two disagree; neither value is
/// coerced, and there is no adjacent-year fallback anywhere in this module.
/// </para>
/// <para>
/// When an accountant has corrected a field, the corrected values are applied to
/// the untrusted provider result before it reaches this method, so every payload
/// — first extraction or twentieth correction — is assembled here and nowhere
/// else.
/// </para>
/// </summary>
public static class ExtractionPayloadBuilder
{
    /// <summary>
    /// Builds the payload and hands it to <paramref name="continuation"/>,
    /// returning whatever the continuation returned.
    /// <para>
    /// Fails closed, and returns errors rather than throwing them, for an absent
    /// provider result, an absent box collection, an absent box entry, and a box
    /// with no code. In each case the continuation never runs, so nothing
    /// partial reaches the engine.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="continuation"/> is null.</exception>
    public static ParseResult<TOutcome> Build<TOutcome>(
        ProviderT4Result? providerResult,
        Func<T4RawInput, ParseResult<TOutcome>> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        if (providerResult is null)
        {
            return Failed<TOutcome>(
                ExtractionWorkflowFields.Provider,
                ExtractionWorkflowErrors.ProviderResultAbsent);
        }

        if (providerResult.Boxes is not { } providerBoxes)
        {
            return Failed<TOutcome>(
                ExtractionWorkflowFields.PayloadBoxes,
                ExtractionWorkflowErrors.PayloadBoxesAbsent);
        }

        var boxes = new List<T4RawBox>(providerBoxes.Count);

        foreach (var providerBox in providerBoxes)
        {
            if (providerBox is null)
            {
                return Failed<TOutcome>(
                    ExtractionWorkflowFields.PayloadBox,
                    ExtractionWorkflowErrors.PayloadBoxAbsent);
            }

            if (string.IsNullOrWhiteSpace(providerBox.Code))
            {
                return Failed<TOutcome>(
                    ExtractionWorkflowFields.PayloadBoxCode,
                    ExtractionWorkflowErrors.PayloadBoxCodeAbsent);
            }

            boxes.Add(new T4RawBox(providerBox.Code, providerBox.Value));
        }

        var payload = new T4RawInput
        {
            Year = providerResult.Year,
            EmployerName = providerResult.EmployerName,
            EmployerAddress = providerResult.EmployerAddress,
            EmployeeName = providerResult.EmployeeName,
            EmployeeAddress = providerResult.EmployeeAddress,
            ExemptCppQpp = providerResult.ExemptCppQpp,
            ExemptEi = providerResult.ExemptEi,
            ExemptPpip = providerResult.ExemptPpip,
            Boxes = boxes,
        };

        return continuation(payload);
    }

    private static ParseResult<TOutcome> Failed<TOutcome>(string field, string message) =>
        ParseResult<TOutcome>.Failure(new[] { new ValidationError(field, message) });
}
