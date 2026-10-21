using MessagingToolbox.Domain.Common;
using MessagingToolbox.Domain.Entities;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Domain.Tests.Entities;

public class CannedMessageTests
{
    [Fact]
    public void Constructor_SetsDefaultFolder()
    {
        CannedMessage message = new();


        message.Folder.ShouldBe(AppConstants.DefaultCannedMessageFolder);
    }

    [Fact]
    public void Constructor_SetsEmptyPropertiesJson()
    {
        CannedMessage message = new();


        message.PropertiesJson.ShouldBe("{}");
    }
}
