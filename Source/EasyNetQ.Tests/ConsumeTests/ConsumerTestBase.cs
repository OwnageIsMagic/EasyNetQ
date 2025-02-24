using EasyNetQ.Consumer;
using EasyNetQ.DI;
using EasyNetQ.Tests.Mocking;
using EasyNetQ.Topology;
using RabbitMQ.Client;

namespace EasyNetQ.Tests.ConsumeTests;

public abstract class ConsumerTestBase : IDisposable
{
    protected const string ConsumerTag = "the_consumer_tag";
    protected const ulong DeliverTag = 10101;
    protected readonly CancellationTokenSource Cancellation;
    protected readonly IConsumeErrorStrategy ConsumeErrorStrategy;
    protected readonly MockBuilder MockBuilder;
    protected bool ConsumerWasInvoked;
    protected ReadOnlyMemory<byte> DeliveredMessageBody;
    protected MessageReceivedInfo DeliveredMessageInfo;
    protected MessageProperties DeliveredMessageProperties;
    protected byte[] OriginalBody;

    // populated when a message is delivered
    protected IBasicProperties OriginalProperties;

    public ConsumerTestBase()
    {
        Cancellation = new CancellationTokenSource();

        ConsumeErrorStrategy = Substitute.For<IConsumeErrorStrategy>();
        MockBuilder = new MockBuilder(x => x.Register(ConsumeErrorStrategy));
        AdditionalSetUp();
    }

    public void Dispose()
    {
        MockBuilder.Dispose();
    }

    protected abstract void AdditionalSetUp();

    protected void StartConsumer(
        Func<ReadOnlyMemory<byte>, MessageProperties, MessageReceivedInfo, AckStrategy> handler,
        bool autoAck = false
    )
    {
        ConsumerWasInvoked = false;
        var queue = new Queue("my_queue", false);
        MockBuilder.Bus.Advanced.Consume(
            queue,
            (body, properties, messageInfo) =>
            {
                return Task.Run(() =>
                {
                    DeliveredMessageBody = body;
                    DeliveredMessageProperties = properties;
                    DeliveredMessageInfo = messageInfo;

                    var ackStrategy = handler(body, properties, messageInfo);
                    ConsumerWasInvoked = true;
                    return ackStrategy;
                }, Cancellation.Token);
            },
            c =>
            {
                if (autoAck)
                    c.WithAutoAck();
                c.WithConsumerTag(ConsumerTag);
            });
    }

    protected void DeliverMessage()
    {
        OriginalProperties = new BasicProperties
        {
            Type = "the_message_type",
            CorrelationId = "the_correlation_id",
        };
        OriginalBody = "Hello World"u8.ToArray();

        MockBuilder.Consumers[0].HandleBasicDeliver(
            ConsumerTag,
            DeliverTag,
            false,
            "the_exchange",
            "the_routing_key",
            OriginalProperties,
            OriginalBody
        ).GetAwaiter().GetResult();
    }
}
