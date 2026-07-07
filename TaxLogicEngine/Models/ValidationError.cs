namespace TaxLogicEngine.Models;

/// <summary>
/// A single, structured problem found while parsing/validating input.
/// <para>
/// Carries the offending <see cref="Field"/> and a human-readable
/// <see cref="Message"/>. Producers of a <see cref="ValidationError"/> are
/// responsible for keeping the message SIN-safe (a SIN must be masked to its
/// last 3 digits — see CLAUDE.md); this type is only a value holder.
/// </para>
/// </summary>
public sealed record ValidationError(string Field, string Message);
