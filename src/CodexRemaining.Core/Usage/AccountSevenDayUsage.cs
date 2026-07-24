namespace CodexRemaining.Core.Usage;

public sealed record AccountDailyTokenBucket(DateOnly Date, long Tokens);

public sealed record AccountSevenDayUsage(long Tokens, DateOnly ThroughDate);

public static class AccountSevenDayUsageFactory
{
    public static AccountSevenDayUsage? TryCreate(IEnumerable<AccountDailyTokenBucket> buckets)
    {
        var values = buckets.ToArray();
        if (values.Length == 0 || values.Any(bucket => bucket.Tokens < 0))
        {
            return null;
        }

        var throughDate = values.Max(bucket => bucket.Date);
        var startDate = throughDate.AddDays(-6);

        try
        {
            var tokens = values
                .Where(bucket => bucket.Date >= startDate && bucket.Date <= throughDate)
                .Aggregate(0L, (total, bucket) => checked(total + bucket.Tokens));
            return new AccountSevenDayUsage(tokens, throughDate);
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
