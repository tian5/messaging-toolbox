using System.Net;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Providers.RabbitMQ.Errors;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.RabbitMQ.Tests.Errors;

public class RabbitMqExceptionTranslatorTests
{
    [Fact]
    public void Translate_ChannelClosedWith404_ReturnsEntityNotFound()
    {
        OperationInterruptedException exception = new(new ShutdownEventArgs(ShutdownInitiator.Peer, 404, "NOT_FOUND - no queue 'orders'"));


        Exception translated = RabbitMqExceptionTranslator.Translate(exception, "queues/orders");


        translated.ShouldBeOfType<MessagingEntityNotFoundException>().EntityPath.ShouldBe("queues/orders");
    }

    [Fact]
    public void Translate_ChannelClosedWith403_ExplainsMissingPermission()
    {
        OperationInterruptedException exception = new(new ShutdownEventArgs(ShutdownInitiator.Peer, 403, "ACCESS_REFUSED"));


        Exception translated = RabbitMqExceptionTranslator.Translate(exception, "queues/orders");


        translated.ShouldBeOfType<MessagingProviderException>().Message.ShouldContain("permission");
    }

    [Fact]
    public void Translate_ManagementUnauthorized_ExplainsTheTagRequirement()
    {
        HttpRequestException exception = new("401", null, HttpStatusCode.Unauthorized);


        Exception translated = RabbitMqExceptionTranslator.Translate(exception, "/");


        translated.ShouldBeOfType<MessagingProviderException>().Message.ShouldContain("management");
    }

    [Fact]
    public void Translate_ManagementUnreachable_ReturnsUnavailable()
    {
        Exception translated = RabbitMqExceptionTranslator.Translate(new HttpRequestException("Connection refused"), "/");


        translated.ShouldBeOfType<MessagingProviderUnavailableException>();
    }

    [Fact]
    public void CanTranslate_UnrelatedException_ReturnsFalse()
    {
        RabbitMqExceptionTranslator.CanTranslate(new InvalidOperationException()).ShouldBeFalse();
    }
}
