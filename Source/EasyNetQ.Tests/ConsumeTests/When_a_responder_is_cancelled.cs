using System.Text;
using EasyNetQ.Events;
using EasyNetQ.Tests.Mocking;

namespace EasyNetQ.Tests.ConsumeTests;

public class When_a_responder_is_cancelled : IDisposable
{
    private readonly MockBuilder mockBuilder;
    private PublishedMessageEvent publishedMessage;
    private AckEvent ackEvent;

    private readonly IConventions conventions;
    private readonly ITypeNameSerializer typeNameSerializer;
    private readonly ISerializer serializer;

    public When_a_responder_is_cancelled()
    {
        mockBuilder = new MockBuilder();

        conventions = mockBuilder.Conventions;
        typeNameSerializer = mockBuilder.TypeNameSerializer;
        serializer = mockBuilder.Serializer;

        mockBuilder.Rpc.Respond<RpcRequest, RpcResponse>(_ =>
        {
            var tcs = new TaskCompletionSource<RpcResponse>();
            tcs.SetCanceled();
            return tcs.Task;
        });

        DeliverMessage(new RpcRequest { Value = 42 });
    }

    public void Dispose()
    {
        mockBuilder.Dispose();
    }

    [Fact]
    public void Should_ACK_with_faulted_response()
    {
        Assert.True((bool)publishedMessage.Properties.Headers["IsFaulted"]);
        Assert.Equal("A task was canceled.", Encoding.UTF8.GetString((byte[])publishedMessage.Properties.Headers["ExceptionMessage"]));
        Assert.Equal(AckResult.Nack, ackEvent.AckResult);
    }

    private void DeliverMessage(RpcRequest request)
    {
        var properties = new BasicProperties
        {
            Type = typeNameSerializer.Serialize(typeof(RpcRequest)),
            CorrelationId = "the_correlation_id",
            ReplyTo = conventions.RpcReturnQueueNamingConvention(typeof(RpcResponse))
        };

        var serializedMessage = serializer.MessageToBytes(typeof(RpcRequest), request);

        var waiter = new CountdownEvent(2);
        mockBuilder.EventBus.Subscribe((in PublishedMessageEvent x) =>
        {
            publishedMessage = x;
            waiter.Signal();
        });
        mockBuilder.EventBus.Subscribe((in AckEvent x) =>
        {
            ackEvent = x;
            waiter.Signal();
        });

        mockBuilder.Consumers[0].HandleBasicDeliver(
            "consumer tag",
            0,
            false,
            "the_exchange",
            "the_routing_key",
            properties,
            serializedMessage.Memory
        ).GetAwaiter().GetResult();

        if (!waiter.Wait(5000))
            throw new TimeoutException();
    }

    private class RpcRequest
    {
        public int Value { get; set; }
    }

    private class RpcResponse
    {
    }
}
