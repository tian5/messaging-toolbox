namespace MessagingToolbox.Application.Common;

/// <summary>Formats sequence numbers for audit-log detail strings: comma-separated, capped at 20.</summary>
internal static class AuditSequenceFormatter
{
    private const int MaxListed = 20;

    public static string Describe(IReadOnlyList<long> sequenceNumbers)
    {
        IEnumerable<string> listed = sequenceNumbers.Take(MaxListed).Select(sequenceNumber => sequenceNumber.ToString());

        return string.Join(", ", listed) + (sequenceNumbers.Count > MaxListed ? ", …" : string.Empty);
    }
}
