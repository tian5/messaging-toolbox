using MessagingToolbox.Application.Common;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Common;

public class MessageBodyFormatterTests
{
    [Fact]
    public void Pretty_JsonBody_IndentsAndKeepsNonAsciiText()
    {
        string pretty = MessageBodyFormatter.Pretty("{\"name\":\"Zürich <HQ>\",\"items\":[1,2]}", MessageBodyFormat.Json);


        pretty.ReplaceLineEndings("\n").ShouldBe("{\n  \"name\": \"Zürich <HQ>\",\n  \"items\": [\n    1,\n    2\n  ]\n}");
    }

    [Fact]
    public void Pretty_XmlBodyWithDeclaration_IndentsAndKeepsDeclaration()
    {
        string pretty = MessageBodyFormatter.Pretty("<?xml version=\"1.0\" encoding=\"utf-8\"?><order><id>7</id></order>", MessageBodyFormat.Xml);


        pretty.ReplaceLineEndings("\n").ShouldBe("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<order>\n  <id>7</id>\n</order>");
    }

    [Fact]
    public void Pretty_XmlBodyWithDtd_ReturnsBodyUnchanged()
    {
        const string body = "<!DOCTYPE order [<!ENTITY x \"y\">]><order>&x;</order>";


        string pretty = MessageBodyFormatter.Pretty(body, MessageBodyFormat.Xml);


        pretty.ShouldBe(body);
    }

    [Theory]
    [InlineData("{broken", MessageBodyFormat.Json)]
    [InlineData("<broken", MessageBodyFormat.Xml)]
    [InlineData("{\"a\":1}", MessageBodyFormat.Text)]
    public void Pretty_UnparsableOrTextBody_ReturnsBodyUnchanged(string body, MessageBodyFormat format)
    {
        MessageBodyFormatter.Pretty(body, format).ShouldBe(body);
    }
}
