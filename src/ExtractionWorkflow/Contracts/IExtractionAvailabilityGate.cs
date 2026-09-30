namespace ExtractionWorkflow.Contracts;

/// <summary>Current approval and dependency availability; never an authorization substitute.</summary>
public interface IExtractionAvailabilityGate
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
}
