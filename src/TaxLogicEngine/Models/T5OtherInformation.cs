namespace TaxLogicEngine.Models;

/// <summary>
/// A single "Other information" code/amount pair from a T5 slip — a box code that
/// is not one of the standard typed boxes on <c>T5Slip</c>. Preserving these
/// verbatim means no provided value is discarded before box → CRA line mapping
/// exists (FR3).
/// </summary>
/// <param name="Code">The box code as printed on the slip (e.g. <c>"30"</c>).</param>
/// <param name="Amount">The monetary amount, in cents-rounded decimal.</param>
public sealed record T5OtherInformation(string Code, decimal Amount);
