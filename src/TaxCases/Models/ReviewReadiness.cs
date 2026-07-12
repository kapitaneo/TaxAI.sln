namespace TaxCases.Models;

public sealed record ReviewReadiness
{
    public ReviewReadiness(ReadinessState state, IReadOnlyList<ReadinessReason> reasons)
    {
        State = state;
        Reasons = Array.AsReadOnly(
            (reasons ?? throw new ArgumentNullException(nameof(reasons))).ToArray());
    }

    public ReadinessState State { get; }

    public IReadOnlyList<ReadinessReason> Reasons { get; }

    public override string ToString() =>
        $"ReviewReadiness {{ State = {State}, ReasonCount = {Reasons.Count} }}";
}
