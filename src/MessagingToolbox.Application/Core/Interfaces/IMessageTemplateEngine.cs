namespace MessagingToolbox.Application.Core.Interfaces;

/// <summary>
/// Renders placeholder templates in message bodies: ~newGuid~, ~now+5m~, ~randomInt~, and
/// environment-variable expansion.
/// </summary>
public interface IMessageTemplateEngine
{
    string Render(string template);
}
