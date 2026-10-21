using MessagingToolbox.Application.Common;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Common;

public class ApplicationPropertyValuesTests
{
    [Theory]
    [InlineData("42", PropertyValueType.Int32, 42)]
    [InlineData("true", PropertyValueType.Boolean, true)]
    [InlineData("1.5", PropertyValueType.Double, 1.5)]
    [InlineData("text", PropertyValueType.String, "text")]
    public void TryParse_ValidText_ReturnsTypedValue(string text, PropertyValueType type, object expected)
    {
        bool parsed = ApplicationPropertyValues.TryParse(text, type, out object? value);


        parsed.ShouldBeTrue();
        value.ShouldBe(expected);
    }

    [Fact]
    public void TryParse_Int64AndGuidAndTimestamp_ReturnsTypedValues()
    {
        ApplicationPropertyValues.TryParse("9000000000", PropertyValueType.Int64, out object? int64);
        ApplicationPropertyValues.TryParse("3f2504e0-4f89-11d3-9a0c-0305e82c3301", PropertyValueType.Guid, out object? guid);


        ApplicationPropertyValues.TryParse("2026-01-31T12:00:00+02:00", PropertyValueType.DateTime, out object? timestamp);


        int64.ShouldBe(9_000_000_000L);
        guid.ShouldBe(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301"));
        timestamp.ShouldBe(new DateTimeOffset(2026, 1, 31, 10, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("abc", PropertyValueType.Int32)]
    [InlineData("1.5", PropertyValueType.Int64)]
    [InlineData("yes", PropertyValueType.Boolean)]
    [InlineData("not-a-guid", PropertyValueType.Guid)]
    [InlineData("", PropertyValueType.Double)]
    public void TryParse_InvalidText_ReturnsFalse(string text, PropertyValueType type)
    {
        ApplicationPropertyValues.TryParse(text, type, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(7, PropertyValueType.Int32, "7")]
    [InlineData(7L, PropertyValueType.Int64, "7")]
    [InlineData(false, PropertyValueType.Boolean, "false")]
    [InlineData(2.25, PropertyValueType.Double, "2.25")]
    [InlineData("hi", PropertyValueType.String, "hi")]
    public void TypeOfAndFormat_BrokerValue_RoundTripsThroughParse(object value, PropertyValueType expectedType, string expectedText)
    {
        PropertyValueType type = ApplicationPropertyValues.TypeOf(value);
        string text = ApplicationPropertyValues.Format(value);


        ApplicationPropertyValues.TryParse(text, type, out object? parsed);


        type.ShouldBe(expectedType);
        text.ShouldBe(expectedText);
        parsed.ShouldBe(value);
    }

    [Fact]
    public void TypeOf_UnspecifiedBrokerTimestamp_FormatsAsUtc()
    {
        DateTime brokerTimestamp = new(2026, 1, 31, 12, 0, 0, DateTimeKind.Unspecified);


        string text = ApplicationPropertyValues.Format(brokerTimestamp);


        ApplicationPropertyValues.TypeOf(brokerTimestamp).ShouldBe(PropertyValueType.DateTime);
        text.ShouldBe("2026-01-31T12:00:00.0000000+00:00");
    }

    [Fact]
    public void IsValidLiteral_TemplatedValue_IsCheckedAtSendTimeInstead()
    {
        ApplicationPropertyValues.IsValidLiteral("~randomInt~", PropertyValueType.Int32).ShouldBeTrue();
    }

    [Fact]
    public void ToTyped_RendersThenParsesAndDefaultsToText()
    {
        Dictionary<string, string> properties = new() { ["count"] = "~n~", ["tenant"] = "tests" };


        Dictionary<string, object?> typed = ApplicationPropertyValues.ToTyped(
            properties,
            new Dictionary<string, PropertyValueType> { ["count"] = PropertyValueType.Int32 },
            text => text.Replace("~n~", "5"));


        typed["count"].ShouldBe(5);
        typed["tenant"].ShouldBe("tests");
    }

    [Fact]
    public void ToTyped_RenderedValueNotOfItsType_ThrowsFormatExceptionNamingTheProperty()
    {
        FormatException exception = Should.Throw<FormatException>(() => ApplicationPropertyValues.ToTyped(
            new Dictionary<string, string> { ["count"] = "many" },
            new Dictionary<string, PropertyValueType> { ["count"] = PropertyValueType.Int32 },
            text => text));


        exception.Message.ShouldContain("'count'");
    }
}
