namespace CodexRemaining.Core.Usage;

// The service count is authoritative; detail rows can be absent or capped.
public sealed record ResetCredits(
    int AvailableCount,
    DateTimeOffset? NearestExpiresAt,
    bool HasCompleteExpiryDetails);
