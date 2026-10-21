using MessagingToolbox.Application.Common;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Common;

public class MessageBodyFormatTests
{
    [Theory]
    [InlineData(null, MessageBodyFormat.Text)]
    [InlineData("", MessageBodyFormat.Text)]
    [InlineData("   ", MessageBodyFormat.Text)]
    public void Detect_EmptyOrWhitespace_ReturnsText(string? body, MessageBodyFormat expected)
    {
        MessageBodyFormatDetector.Detect(body!).ShouldBe(expected);
    }

    [Theory]
    [InlineData("{\"orderId\": 7}", MessageBodyFormat.Json)]
    [InlineData("[1, 2, 3]", MessageBodyFormat.Json)]
    public void Detect_ValidJson_ReturnsJson(string body, MessageBodyFormat expected)
    {
        MessageBodyFormatDetector.Detect(body).ShouldBe(expected);
    }

    [Theory]
    [InlineData("<root />", MessageBodyFormat.Xml)]
    [InlineData("<root>value</root>", MessageBodyFormat.Xml)]
    [InlineData("<?xml version=\"1.0\"?><root />", MessageBodyFormat.Xml)]
    [InlineData("  <root />", MessageBodyFormat.Xml)]
    public void Detect_ValidXml_ReturnsXml(string body, MessageBodyFormat expected)
    {
        MessageBodyFormatDetector.Detect(body).ShouldBe(expected);
    }

    [Theory]
    [InlineData("plain body", MessageBodyFormat.Text)]
    [InlineData("<not valid xml", MessageBodyFormat.Text)]
    [InlineData("{not json}", MessageBodyFormat.Text)]
    [InlineData(" \"string value\" ", MessageBodyFormat.Text)]
    [InlineData("42", MessageBodyFormat.Text)]
    [InlineData("true", MessageBodyFormat.Text)]
    [InlineData("null", MessageBodyFormat.Text)]
    public void Detect_NonStructuredText_ReturnsText(string body, MessageBodyFormat expected)
    {
        MessageBodyFormatDetector.Detect(body).ShouldBe(expected);
    }
}
