using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.RabbitMQ.Tests;

public class RabbitMqConnectionSettingsTests
{
    [Fact]
    public void Parse_PlainAmqpUri_DerivesManagementOnPort15672AndDefaultVhost()
    {
        RabbitMqConnectionSettings settings = RabbitMqConnectionSettings.Parse("amqp://user:p%40ss@broker:5672", null);


        settings.ManagementUri.ShouldBe(new Uri("http://broker:15672/"));
        settings.VirtualHost.ShouldBe("/");
        settings.UserName.ShouldBe("user");
        settings.Password.ShouldBe("p@ss");
    }

    [Fact]
    public void Parse_AmqpsUriWithVhost_DerivesTlsManagementPortAndUnescapesVhost()
    {
        RabbitMqConnectionSettings settings = RabbitMqConnectionSettings.Parse("amqps://user:pw@broker/team%2Fa", null);


        settings.ManagementUri.ShouldBe(new Uri("https://broker:15671/"));
        settings.VirtualHost.ShouldBe("team/a");
    }

    [Fact]
    public void Parse_ManagementSuffix_UsesItAndStripsItFromTheAmqpUri()
    {
        RabbitMqConnectionSettings settings = RabbitMqConnectionSettings.Parse("amqp://u:p@broker:5672/;Management=http://admin:8080", null);


        settings.ManagementUri.ShouldBe(new Uri("http://admin:8080/"));
        settings.AmqpUri.ShouldBe(new Uri("amqp://u:p@broker:5672/"));
    }

    [Fact]
    public void Parse_HostSuppliedEndpoint_WinsOverSuffixAndDefault()
    {
        RabbitMqConnectionSettings settings = RabbitMqConnectionSettings.Parse("amqp://u:p@broker;Management=http://admin:8080", "http://localhost:51234");


        settings.ManagementUri.ShouldBe(new Uri("http://localhost:51234/"));
    }

    [Fact]
    public void Parse_NoCredentials_FallsBackToGuest()
    {
        RabbitMqConnectionSettings settings = RabbitMqConnectionSettings.Parse("amqp://localhost", null);


        settings.UserName.ShouldBe("guest");
        settings.Password.ShouldBe("guest");
    }

    [Theory]
    [InlineData("Endpoint=sb://localhost;SharedAccessKeyName=x;SharedAccessKey=y")]
    [InlineData("not a uri")]
    public void Parse_NotAnAmqpUri_ThrowsFormatException(string connectionString)
    {
        Should.Throw<FormatException>(() => RabbitMqConnectionSettings.Parse(connectionString, null));
    }
}
