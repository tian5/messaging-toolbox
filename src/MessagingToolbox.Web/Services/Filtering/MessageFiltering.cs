using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Web.Services.Filtering;

/// <summary>Pure filter/sort logic for the message grid: key=value or free-text filtering, then sequence ordering.</summary>
public static class MessageFiltering
{
    public static IReadOnlyList<MessageDto>? Apply(
        IReadOnlyList<MessageDto>? messages,
        string? propertyFilter,
        bool sortDescending)
    {
        if (messages is null)
        {
            return null;
        }

        IEnumerable<MessageDto> filtered = messages;

        if (!string.IsNullOrWhiteSpace(propertyFilter))
        {
            string filter = propertyFilter.Trim();
            int separator = filter.IndexOf('=');
            if (separator > 0)
            {
                string key = filter[..separator].Trim();
                string value = filter[(separator + 1)..].Trim();
                filtered = filtered.Where(message => message.ApplicationProperties.Any(pair =>
                    pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase) &&
                    pair.Value?.ToString()?.Contains(value, StringComparison.OrdinalIgnoreCase) == true));
            }
            else
            {
                filtered = filtered.Where(message =>
                    message.Subject?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true ||
                    message.MessageId?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);
            }
        }

        filtered = sortDescending
            ? filtered.OrderByDescending(message => message.SequenceNumber ?? long.MinValue)
            : filtered.OrderBy(message => message.SequenceNumber ?? long.MaxValue);

        return filtered.ToList();
    }
}
