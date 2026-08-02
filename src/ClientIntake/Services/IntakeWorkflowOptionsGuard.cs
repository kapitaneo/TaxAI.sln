using ClientIntake.Models;

namespace ClientIntake.Services;

internal static class IntakeWorkflowOptionsGuard
{
    internal static IntakeWorkflowOptions RequireValid(IntakeWorkflowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.LinkLifetime <= TimeSpan.Zero ||
            options.ClientSessionIdleLifetime <= TimeSpan.Zero ||
            options.MaximumFileBytes <= 0 ||
            options.MaximumFilesPerRequest <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        return options;
    }

    internal static bool IsUtc(DateTimeOffset instant) =>
        instant.Offset == TimeSpan.Zero;

    internal static bool TryAdd(
        DateTimeOffset instant,
        TimeSpan lifetime,
        out DateTimeOffset boundary)
    {
        try
        {
            boundary = instant.Add(lifetime);
            return boundary > instant;
        }
        catch (ArgumentOutOfRangeException)
        {
            boundary = default;
            return false;
        }
    }

    internal static DateTimeOffset RenewIdle(
        DateTimeOffset nowUtc,
        TimeSpan idleLifetime,
        DateTimeOffset absoluteExpiresAtUtc)
    {
        if (!TryAdd(nowUtc, idleLifetime, out var proposed))
        {
            return absoluteExpiresAtUtc;
        }

        return proposed < absoluteExpiresAtUtc
            ? proposed
            : absoluteExpiresAtUtc;
    }
}
