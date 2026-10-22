using FluentValidation.TestHelper;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Features.Messages.Commands.RequeueMessages;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Commands.RequeueMessages;

public class RequeueMessagesCommandValidatorTests
{
    private static readonly MessageDto Message = new()
    {
        Body = "{}",
        BodyFormat = MessageBodyFormat.Json,
        BodySizeBytes = 2,
        MessageId = "msg-1",
    };

    private readonly RequeueMessagesCommandValidator _sut = new();

    [Fact]
    public void Validate_ScheduledSubQueue_HasValidationErrorForSourceSubQueue()
    {
        _sut.TestValidate(new RequeueMessagesCommand(Guid.NewGuid(), "orders", MessageSubQueue.Scheduled, "retry-queue", [Message]))
            .ShouldHaveValidationErrorFor(command => command.SourceSubQueue);
    }

    // Whether an active queue may be a source depends on the broker; the handler decides (ADR 19).
    [Theory]
    [InlineData(MessageSubQueue.Active)]
    [InlineData(MessageSubQueue.DeadLetter)]
    [InlineData(MessageSubQueue.TransferDeadLetter)]
    public void Validate_NonScheduledSubQueue_DoesNotHaveValidationErrorForSourceSubQueue(MessageSubQueue subQueue)
    {
        _sut.TestValidate(new RequeueMessagesCommand(Guid.NewGuid(), "orders/$deadletter", subQueue, "retry-queue", [Message]))
            .ShouldNotHaveValidationErrorFor(command => command.SourceSubQueue);
    }

    [Fact]
    public void Validate_EmptyTargetPath_HasValidationErrorForTargetPath()
    {
        _sut.TestValidate(new RequeueMessagesCommand(Guid.NewGuid(), "orders/$deadletter", MessageSubQueue.DeadLetter, "", [Message]))
            .ShouldHaveValidationErrorFor(command => command.TargetPath);
    }

    [Fact]
    public void Validate_EmptyMessages_HasValidationError()
    {
        _sut.TestValidate(new RequeueMessagesCommand(Guid.NewGuid(), "orders/$deadletter", MessageSubQueue.DeadLetter, "retry-queue", []))
            .ShouldHaveValidationErrorFor(command => command);
    }

    [Fact]
    public void Validate_RemoveOriginalsWithoutLockTokens_DoesNotHaveValidationError()
    {
        _sut.TestValidate(new RequeueMessagesCommand(
                Guid.NewGuid(), "orders/$deadletter", MessageSubQueue.DeadLetter, "retry-queue", [Message], RemoveOriginals: true))
            .ShouldNotHaveValidationErrorFor(command => command);
    }

    [Fact]
    public void ValidCommand_DoesNotHaveAnyValidationErrors()
    {
        _sut.TestValidate(new RequeueMessagesCommand(
                Guid.NewGuid(), "orders/$deadletter", MessageSubQueue.DeadLetter, "retry-queue", [Message], LockTokens: ["token-1"], RemoveOriginals: true))
            .ShouldNotHaveAnyValidationErrors();
    }
}
