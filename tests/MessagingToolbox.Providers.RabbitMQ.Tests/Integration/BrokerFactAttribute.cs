using Xunit;

namespace MessagingToolbox.Providers.RabbitMQ.Tests.Integration;

/// <summary>
/// A test against a real RabbitMQ container (Docker). Skipped unless <see cref="EnableVariable"/>
/// is <c>true</c> — the same switch as the Service Bus emulator tests, so the CI emulator job runs
/// both. Classes using it carry <c>[Trait("Category", "Emulator")]</c> for filtering.
/// </summary>
public sealed class BrokerFactAttribute : FactAttribute
{
    public const string EnableVariable = "MESSAGINGTOOLBOX_EMULATOR_TESTS";

    public BrokerFactAttribute()
    {
        if (!IsEnabled)
        {
            Skip = $"Needs Docker and a RabbitMQ container; set {EnableVariable}=true to run.";
        }
    }

    public static bool IsEnabled
        => string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "true", StringComparison.OrdinalIgnoreCase);
}
