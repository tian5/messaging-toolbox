using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Web.Components.Messages;

/// <summary>A settlement choice from the settle bar, including optional dead-letter reason fields (FR-5.1).</summary>
public sealed record SettleAction(SettlementOperation Operation, string? Reason, string? Description);
