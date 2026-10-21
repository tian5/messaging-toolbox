using Mediator;
using MessagingToolbox.Application.Core.Interfaces;

namespace MessagingToolbox.Application.Features.Templates.Queries.RenderTemplate;

public sealed class RenderTemplateQueryHandler : IRequestHandler<RenderTemplateQuery, string>
{
    private readonly IMessageTemplateEngine _templateEngine;

    public RenderTemplateQueryHandler(IMessageTemplateEngine templateEngine)
    {
        _templateEngine = templateEngine;
    }

    public ValueTask<string> Handle(RenderTemplateQuery request, CancellationToken cancellationToken)
        => ValueTask.FromResult(_templateEngine.Render(request.Template));
}
