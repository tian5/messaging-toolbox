using MessagingToolbox.Application.Common;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Common;

public class CannedPropertiesJsonTests
{
    [Fact]
    public void Serialize_TypedAndTextProperties_RoundTripsValuesAndTypes()
    {
        Dictionary<string, string> properties = new() { ["count"] = "42", ["tenant"] = "tests" };
        Dictionary<string, PropertyValueType> types = new() { ["count"] = PropertyValueType.Int32, ["tenant"] = PropertyValueType.String };


        (Dictionary<string, string> readProperties, Dictionary<string, PropertyValueType> readTypes) =
            CannedPropertiesJson.Deserialize(CannedPropertiesJson.Serialize(properties, types));


        readProperties.ShouldBe(properties);
        readTypes.ShouldBe(new Dictionary<string, PropertyValueType> { ["count"] = PropertyValueType.Int32 });
    }

    [Fact]
    public void Serialize_TextOnly_KeepsTheOriginalPlainFormat()
    {
        string json = CannedPropertiesJson.Serialize(new Dictionary<string, string> { ["tenant"] = "tests" }, null);


        json.ShouldBe("""{"tenant":"tests"}""");
    }

    [Fact]
    public void Deserialize_LegacyPlainJson_ReadsAllAsText()
    {
        (Dictionary<string, string> properties, Dictionary<string, PropertyValueType> types) =
            CannedPropertiesJson.Deserialize("""{"tenant":"tests","region":"eu"}""");


        properties.ShouldBe(new Dictionary<string, string> { ["tenant"] = "tests", ["region"] = "eu" });
        types.ShouldBeEmpty();
    }
}
