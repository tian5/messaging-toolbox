using System.Net;
using System.Text;
using MessagingToolbox.Providers.RabbitMQ.Management;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.RabbitMQ.Tests.Management;

public class RabbitMqManagementClientTests
{
    private readonly StubHandler _handler = new();
    private readonly RabbitMqManagementClient _sut;

    public RabbitMqManagementClientTests()
    {
        _sut = new RabbitMqManagementClient(new HttpClient(_handler), new Uri("http://broker:15672/"), "/", "guest", "secret");
    }

    [Fact]
    public async Task ListQueuesAsync_DefaultVhost_EscapesVhostAndMapsColumns()
    {
        _handler.Respond("""[{"name":"orders","type":"quorum","messages":3,"messages_ready":2,"messages_unacknowledged":1,"durable":true,"consumers":2,"policy":"ttl"},{"name":"activity","type":"stream"}]""");


        IReadOnlyList<RabbitMqQueueInfo> queues = await _sut.ListQueuesAsync(CancellationToken.None);


        _handler.LastRequest!.RequestUri!.AbsoluteUri.ShouldStartWith("http://broker:15672/api/queues/%2F?columns=");
        _handler.LastRequest.Headers.Authorization!.Parameter.ShouldBe(Convert.ToBase64String(Encoding.UTF8.GetBytes("guest:secret")));
        queues.Count.ShouldBe(2);
        queues[0].Messages.ShouldBe(3);
        queues[0].IsStream.ShouldBeFalse();
        queues[0].Durable.ShouldBe(true);
        queues[0].Consumers.ShouldBe(2);
        queues[0].Policy.ShouldBe("ttl");
        queues[1].IsStream.ShouldBeTrue();
        queues[1].Messages.ShouldBeNull();
    }

    [Fact]
    public async Task GetQueueAsync_NameWithSlash_EscapesName()
    {
        _handler.Respond("""{"name":"a/b","type":"classic","messages":0}""");


        RabbitMqQueueInfo queue = await _sut.GetQueueAsync("a/b", CancellationToken.None);


        _handler.LastRequest!.RequestUri!.AbsoluteUri.ShouldBe("http://broker:15672/api/queues/%2F/a%2Fb");
        queue.Name.ShouldBe("a/b");
    }

    [Fact]
    public async Task ProbeAsync_Unauthorized_ThrowsWithStatusCode()
    {
        _handler.Respond(string.Empty, HttpStatusCode.Unauthorized);


        HttpRequestException exception = await Should.ThrowAsync<HttpRequestException>(() => _sut.ProbeAsync(CancellationToken.None));


        exception.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeclareQueueAsync_Declaration_PutsFieldsAndArguments()
    {
        _handler.Respond(string.Empty, HttpStatusCode.Created);


        await _sut.DeclareQueueAsync("orders", new RabbitMqQueueDeclaration(true, false, new Dictionary<string, object> { ["x-message-ttl"] = 5000L }), CancellationToken.None);


        _handler.LastRequest!.Method.ShouldBe(HttpMethod.Put);
        _handler.LastRequest.RequestUri!.AbsoluteUri.ShouldBe("http://broker:15672/api/queues/%2F/orders");
        _handler.LastBody.ShouldBe("""{"durable":true,"auto_delete":false,"arguments":{"x-message-ttl":5000}}""");
    }

    [Fact]
    public async Task DeclareExchangeAsync_Refused_ThrowsWithTheBrokersReason()
    {
        _handler.Respond("""{"error":"bad_request","reason":"inequivalent arg 'type' for exchange 'events'"}""", HttpStatusCode.BadRequest);


        HttpRequestException exception = await Should.ThrowAsync<HttpRequestException>(() => _sut.DeclareExchangeAsync(
            "events", new RabbitMqExchangeDeclaration("topic", true, false, false, new Dictionary<string, object>()), CancellationToken.None));


        exception.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        exception.Message.ShouldBe("inequivalent arg 'type' for exchange 'events'");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private string _body = "[]";
        private HttpStatusCode _status = HttpStatusCode.OK;

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        public void Respond(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
