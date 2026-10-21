using MessagingToolbox.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Services;

public class MessageTemplateEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly MessageTemplateEngine _sut;

    public MessageTemplateEngineTests()
    {
        _sut = new MessageTemplateEngine(NullLogger<MessageTemplateEngine>.Instance, new FixedTimeProvider(Now));
    }

    [Fact]
    public void Render_NewGuidTemplate_ReturnsDistinctParsableGuids()
    {
        string first = _sut.Render("~newGuid~");
        string second = _sut.Render("~newGuid~");


        Guid.TryParse(first, out _).ShouldBeTrue();
        Guid.TryParse(second, out _).ShouldBeTrue();
        first.ShouldNotBe(second);
    }

    [Fact]
    public void Render_NowOffsetTemplate_ReturnsFormattedOffsetTime()
    {
        _sut.Render("~now+5m~").ShouldBe(Now.AddMinutes(5).ToString("O"));
        _sut.Render("~now-1d~").ShouldBe(Now.AddDays(-1).ToString("O"));
        _sut.Render("~now+30s~").ShouldBe(Now.AddSeconds(30).ToString("O"));
    }

    [Fact]
    public void Render_RandomIntTemplate_ReturnsValueInRange()
    {
        for (int i = 0; i < 50; i++)
        {
            int value = int.Parse(_sut.Render("~randomInt(3,9)~"));
            value.ShouldBeGreaterThanOrEqualTo(3);
            value.ShouldBeLessThan(9);
        }
    }

    [Fact]
    public void Render_EnvVariableTemplate_ExpandsEnvironmentVariable()
    {
        Environment.SetEnvironmentVariable("MT_TEST_VAR", "expanded");


        try
        {
            _sut.Render("prefix-~env:MT_TEST_VAR~").ShouldBe("prefix-expanded");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MT_TEST_VAR", null);
        }
    }

    [Fact]
    public void Render_MissingEnvVariableTemplate_RendersEmptyString()
    {
        _sut.Render("prefix-~env:MT_DEFINITELY_MISSING_VAR~").ShouldBe("prefix-");
    }

    [Fact]
    public void Render_UnknownPlaceholderTemplate_ReturnsInputUnchanged()
    {
        _sut.Render("keep ~notAPlaceholder~ here").ShouldBe("keep ~notAPlaceholder~ here");
    }

    [Fact]
    public void Render_PlainText_ReturnsInputUnchanged()
    {
        _sut.Render("{\"orderId\": 7}").ShouldBe("{\"orderId\": 7}");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
