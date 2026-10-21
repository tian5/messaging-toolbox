using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Web.Services.Helpers;

/// <summary>Human-readable labels for the sub-queue selector shown next to the load controls.</summary>
public static class SubQueueLabels
{
    public static string LabelOf(MessageSubQueue subQueue) => subQueue switch
    {
        MessageSubQueue.DeadLetter => "dead-letter sub-queue",
        MessageSubQueue.TransferDeadLetter => "transfer dead-letter sub-queue",
        MessageSubQueue.Scheduled => "scheduled messages",
        _ => "active messages",
    };
}
