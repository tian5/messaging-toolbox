using Mediator;

namespace MessagingToolbox.Application.Features.Templates.Queries.RenderTemplate;

/// <summary>Renders placeholder templating for a composer preview.</summary>
public sealed record RenderTemplateQuery(string Template) : IRequest<string>;
