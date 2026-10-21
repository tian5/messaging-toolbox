using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests.Integration;

/// <summary>
/// A test against the real Azure Service Bus emulator (Docker, two containers, slow). Skipped
/// unless <see cref="EnableVariable"/> is <c>true</c>, so a plain <c>dotnet test</c> never starts
/// containers; the CI emulator job sets it. Classes using it carry
/// <c>[Trait("Category", "Emulator")]</c> for filtering.
/// </summary>
public sealed class EmulatorFactAttribute : FactAttribute
{
    public const string EnableVariable = "MESSAGINGTOOLBOX_EMULATOR_TESTS";

    public EmulatorFactAttribute()
    {
        if (!IsEnabled)
        {
            Skip = $"Needs Docker and the Service Bus emulator; set {EnableVariable}=true to run.";
        }
    }

    public static bool IsEnabled
        => string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "true", StringComparison.OrdinalIgnoreCase);
}
