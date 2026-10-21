using Azure.Messaging.ServiceBus;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Providers.AzureServiceBus.Errors;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.AzureServiceBus.Tests.Errors;

public class ServiceBusExceptionTranslatorTests
{
    [Fact]
    public void Translate_MessageLockLost_ReturnsLockLostException()
    {
        ServiceBusException exception = new("lock gone", ServiceBusFailureReason.MessageLockLost);

        Exception translated = ServiceBusExceptionTranslator.Translate(exception, "orders");

        translated.ShouldBeOfType<MessageLockLostException>();
    }

    [Fact]
    public void Translate_EntityNotFound_ReturnsEntityNotFoundExceptionWithPath()
    {
        ServiceBusException exception = new("missing", ServiceBusFailureReason.MessagingEntityNotFound);


        Exception translated = ServiceBusExceptionTranslator.Translate(exception, "orders");


        MessagingEntityNotFoundException typed = translated.ShouldBeOfType<MessagingEntityNotFoundException>();
        typed.EntityPath.ShouldBe("orders");
    }

    [Fact]
    public void Translate_MessageSizeExceeded_ReturnsMessagingProviderException()
    {
        ServiceBusException exception = new("too big", ServiceBusFailureReason.MessageSizeExceeded);

        Exception translated = ServiceBusExceptionTranslator.Translate(exception, "orders");

        translated.ShouldBeOfType<MessagingProviderException>();
    }

    [Fact]
    public void Translate_ServiceBusy_ReturnsThrottledException()
    {
        ServiceBusException exception = new("busy", ServiceBusFailureReason.ServiceBusy);

        Exception translated = ServiceBusExceptionTranslator.Translate(exception, "orders");

        MessagingProviderThrottledException typed = translated.ShouldBeOfType<MessagingProviderThrottledException>();
        typed.InnerException.ShouldBeSameAs(exception);
    }

    [Theory]
    [InlineData(ServiceBusFailureReason.ServiceTimeout)]
    [InlineData(ServiceBusFailureReason.ServiceCommunicationProblem)]
    public void Translate_BrokerUnreachableOrSlow_ReturnsUnavailableException(ServiceBusFailureReason reason)
    {
        ServiceBusException exception = new("no answer", reason);


        Exception translated = ServiceBusExceptionTranslator.Translate(exception, "orders");


        translated.ShouldBeOfType<MessagingProviderUnavailableException>().InnerException.ShouldBeSameAs(exception);
    }

    [Theory]
    [InlineData(ServiceBusFailureReason.MessagingEntityDisabled)]
    [InlineData(ServiceBusFailureReason.MessagingEntityAlreadyExists)]
    [InlineData(ServiceBusFailureReason.QuotaExceeded)]
    [InlineData(ServiceBusFailureReason.SessionCannotBeLocked)]
    [InlineData(ServiceBusFailureReason.GeneralError)]
    public void Translate_OtherReasons_ReturnsTypedProviderExceptionNamingTheEntity(ServiceBusFailureReason reason)
    {
        ServiceBusException exception = new("rejected", reason);


        Exception translated = ServiceBusExceptionTranslator.Translate(exception, "orders");


        translated.ShouldBeOfType<MessagingProviderException>().Message.ShouldContain("orders");
    }

    [Fact]
    public void Translate_UnauthorizedAccess_ReturnsProviderException()
    {
        UnauthorizedAccessException exception = new("401");


        Exception translated = ServiceBusExceptionTranslator.Translate(exception, "orders");


        translated.ShouldBeOfType<MessagingProviderException>().InnerException.ShouldBeSameAs(exception);
    }

    [Fact]
    public void Translate_AdminEndpointUnreachable_ReturnsUnavailableException()
    {
        HttpRequestException exception = new("connection refused");


        Exception translated = ServiceBusExceptionTranslator.Translate(exception, "orders");


        translated.ShouldBeOfType<MessagingProviderUnavailableException>();
    }

    [Fact]
    public void CanTranslate_UnrelatedException_ReturnsFalse()
    {
        bool result = ServiceBusExceptionTranslator.CanTranslate(new InvalidOperationException());


        result.ShouldBeFalse();
    }

    [Fact]
    public void Translate_SessionReceiverOnSessionlessEntity_SaysToClearTheFilter()
    {
        InvalidOperationException exception = new(
            "A sessionful message receiver cannot be created on an entity that does not require sessions.");


        Exception translated = ServiceBusExceptionTranslator.Translate(exception, "orders");


        translated.ShouldBeOfType<MessagingProviderException>().Message.ShouldContain("doesn't use sessions");
    }

    [Theory]
    [InlineData("It is not possible for an entity that requires sessions to create a non-sessionful message receiver.")]
    [InlineData("The SessionId was not set on a message, and it cannot be sent to the entity.")]
    public void IsSessionRequirement_BrokerSessionRejection_ReturnsTrue(string message)
    {
        bool result = ServiceBusExceptionTranslator.IsSessionRequirement(new InvalidOperationException(message));


        result.ShouldBeTrue();
    }
}
