using System.Text.RegularExpressions;
using MessagingToolbox.Application.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace MessagingToolbox.Application.Services;

/// <summary>
/// Placeholder templating for message bodies and property values:
/// <c>~newGuid~</c>, <c>~now±Ns|m|h|d~</c>, <c>~randomInt~</c> / <c>~randomInt(min,max)~</c>,
/// and environment-variable expansion via <c>~env:NAME~</c> (missing variables render empty and
/// are logged, never silently kept). Zero external dependencies.
/// </summary>
public sealed partial class MessageTemplateEngine : IMessageTemplateEngine
{
    private readonly ILogger<MessageTemplateEngine> _logger;
    private readonly TimeProvider _timeProvider;

    public MessageTemplateEngine(ILogger<MessageTemplateEngine> logger, TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public string Render(string template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        return PlaceholderPattern().Replace(template, ReplaceMatch);
    }

    private string ReplaceMatch(Match match)
    {
        string token = match.Groups[1].Value;

        if (token.Equals("newGuid", StringComparison.OrdinalIgnoreCase))
        {
            return Guid.NewGuid().ToString();
        }

        if (token.Equals("randomInt", StringComparison.OrdinalIgnoreCase))
        {
            return Random.Shared.Next().ToString();
        }

        Match randomRange = RandomIntRangePattern().Match(token);
        if (randomRange.Success
            && int.TryParse(randomRange.Groups[1].Value, out int min)
            && int.TryParse(randomRange.Groups[2].Value, out int max)
            && min < max)
        {
            return Random.Shared.Next(min, max).ToString();
        }

        Match nowOffset = NowOffsetPattern().Match(token);
        if (nowOffset.Success && long.TryParse(nowOffset.Groups[2].Value, out long amount))
        {
            char sign = nowOffset.Groups[1].Value[0];
            TimeSpan offset = nowOffset.Groups[3].Value switch
            {
                "s" => TimeSpan.FromSeconds(amount),
                "m" => TimeSpan.FromMinutes(amount),
                "h" => TimeSpan.FromHours(amount),
                "d" => TimeSpan.FromDays(amount),
                _ => TimeSpan.Zero,
            };

            DateTimeOffset value = _timeProvider.GetUtcNow() + (sign == '-' ? -offset : offset);
            return value.ToString("O");
        }

        if (token.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            string name = token["env:".Length..];
            string? value = Environment.GetEnvironmentVariable(name);
            if (value is null)
            {
                _logger.LogWarning("Template references unknown environment variable {Name}; rendering empty", name);
            }

            return value ?? string.Empty;
        }

        _logger.LogWarning("Unknown template placeholder ~{Token}~; left as-is", token);
        return match.Value;
    }

    [GeneratedRegex(@"~([^~]+)~")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"^randomInt\((-?\d+),(-?\d+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RandomIntRangePattern();

    [GeneratedRegex(@"^now([+-])(\d+)(s|m|h|d)$", RegexOptions.IgnoreCase)]
    private static partial Regex NowOffsetPattern();
}
