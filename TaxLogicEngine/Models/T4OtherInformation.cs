namespace TaxLogicEngine.Models;

/// <summary>
/// A single "Other information" code/amount pair from a T4 slip — a box code that
/// is not one of the standard typed boxes on <c>T4Slip</c>. Preserving these
/// verbatim means no provided value is discarded before box → CRA line mapping
/// exists (spec 004). An unknown/future code is passthrough, never an error.
/// </summary>
/// <param name="Code">The box code as printed on the slip (e.g. <c>"40"</c>).</param>
/// <param name="Amount">The monetary amount, in cents-rounded decimal.</param>
public sealed record T4OtherInformation(string Code, decimal Amount);
