using FluentValidation.TestHelper;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Features.Messages.Queries.ReceiveDeferredMessages;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Queries.ReceiveDeferredMessages;

public class ReceiveDeferredMessagesQueryValidatorTests
{
    private readonly ReceiveDeferredMessagesQueryValidator _sut = new();

    [Theory]
    [InlineData(MessageSubQueue.Active)]
    [InlineData(MessageSubQueue.DeadLetter)]
    public void Validate_DeferredMessagesOnReceivableSubQueue_IsValid(MessageSubQueue subQueue)
    {
        _sut.TestValidate(new ReceiveDeferredMessagesQuery(Guid.NewGuid(), "orders", subQueue, [new DeferredMessageReference(7)], 60))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ScheduledSubQueue_HasValidationErrorForSubQueue()
    {
        _sut.TestValidate(new ReceiveDeferredMessagesQuery(Guid.NewGuid(), "orders", MessageSubQueue.Scheduled, [new DeferredMessageReference(7)]))
            .ShouldHaveValidationErrorFor(query => query.SubQueue);
    }

    [Fact]
    public void Validate_NoMessages_HasValidationErrorForMessages()
    {
        _sut.TestValidate(new ReceiveDeferredMessagesQuery(Guid.NewGuid(), "orders", MessageSubQueue.Active, []))
            .ShouldHaveValidationErrorFor(query => query.Messages);
    }

    [Fact]
    public void Validate_TooManyMessages_HasValidationErrorForMessages()
    {
        List<DeferredMessageReference> messages = Enumerable.Range(1, ReceiveDeferredMessagesQueryValidator.MaxMessages + 1)
            .Select(sequenceNumber => new DeferredMessageReference(sequenceNumber))
            .ToList();


        _sut.TestValidate(new ReceiveDeferredMessagesQuery(Guid.NewGuid(), "orders", MessageSubQueue.Active, messages))
            .ShouldHaveValidationErrorFor(query => query.Messages);
    }
}
