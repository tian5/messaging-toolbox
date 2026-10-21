using Mediator;
using Microsoft.AspNetCore.Components;

namespace MessagingToolbox.Web.Components;

/// <summary>
/// Base for toolbox components — components talk to the application layer through Mediator only,
/// mirroring ApiControllerBase from the code style conventions. No business logic in components.
/// </summary>
public abstract class ToolboxComponentBase : ComponentBase
{
    [Inject]
    protected ISender Mediator { get; private set; } = default!;
}
