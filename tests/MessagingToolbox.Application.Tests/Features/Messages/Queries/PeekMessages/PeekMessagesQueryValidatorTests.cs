using FluentValidation.TestHelper;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Features.Messages.Queries.PeekMessages;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Queries.PeekMessages;

public class PeekMessagesQueryValidatorTests
{
    private readonly PeekMessagesQueryValidator _sut = new();

    [Fact]
    public void Validate_SequenceNumberAndTime_HasValidationErrorForTime()
    {
        _sut.TestValidate(new PeekMessagesQuery(Guid.NewGuid(), "queues/activity", MessageSubQueue.Active, 5, 10, null, DateTimeOffset.UnixEpoch))
            .ShouldHaveValidationErrorFor(query => query.FromTime);
    }

    [Fact]
    public void Validate_TimeOnly_IsValid()
    {
        _sut.TestValidate(new PeekMessagesQuery(Guid.NewGuid(), "queues/activity", MessageSubQueue.Active, null, 10, null, DateTimeOffset.UnixEpoch))
            .ShouldNotHaveAnyValidationErrors();
    }
}
