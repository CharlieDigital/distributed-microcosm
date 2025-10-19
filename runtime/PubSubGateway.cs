using Google.Api.Gax;
using Google.Cloud.PubSub.V1;
using Grpc.Core;

public class PubSubGateway
{
    // 👇 The signal to be used for readiness
    public TaskCompletionSource ReadySignal { get; } = new();

    private readonly PublisherServiceApiClient _publisherClient;
    private readonly SubscriberServiceApiClient _subscriberClient;
    private readonly string _projectId;

    public PubSubGateway(string endpoint, string projectId)
    {
        _publisherClient = new PublisherServiceApiClientBuilder
        {
            EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
            ChannelCredentials = ChannelCredentials.Insecure,
            Endpoint = endpoint,
        }.Build();

        _subscriberClient = new SubscriberServiceApiClientBuilder
        {
            EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
            ChannelCredentials = ChannelCredentials.Insecure,
            Endpoint = endpoint,
        }.Build();

        _projectId = projectId;
    }

    // 👇 Verify if a topic exists, optionally creating it if it does not
    public async Task<bool> VerifyTopicExists(string topicId, bool createIfNotExists = false)
    {
        var topicName = TopicName.FromProjectTopic(_projectId, topicId);

        try
        {
            var topic = await _publisherClient.GetTopicAsync(topicName);

            return true;
        }
        catch (RpcException ex) when (ex.Status.StatusCode == StatusCode.NotFound)
        {
            if (createIfNotExists)
            {
                await _publisherClient.CreateTopicAsync(topicName);
                return true;
            }

            return false;
        }
    }

    // 👇 Verify if a subscription exists, optionally creating it if it does not
    public async Task<bool> VerifySubscriptionExists(
        string topicId,
        string subscriptionId,
        bool createIfNotExists = false
    )
    {
        var subscriptionName = SubscriptionName.FromProjectSubscription(_projectId, subscriptionId);

        try
        {
            var subscription = await _subscriberClient.GetSubscriptionAsync(subscriptionName);

            return true;
        }
        catch (RpcException ex) when (ex.Status.StatusCode == StatusCode.NotFound)
        {
            if (createIfNotExists)
            {
                await _subscriberClient.CreateSubscriptionAsync(
                    subscriptionName,
                    TopicName.FromProjectTopic(_projectId, topicId),
                    pushConfig: null,
                    ackDeadlineSeconds: 60
                );
                return true;
            }

            return false;
        }
    }

    public async Task<IReadOnlyList<(PubsubMessage Message, string AckId)>> PullMessagesAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default
    )
    {
        var subscriptionName = SubscriptionName.FromProjectSubscription(_projectId, subscriptionId);

        var response = await _subscriberClient.PullAsync(
            subscriptionName,
            maxMessages: 10,
            cancellationToken: cancellationToken
        );

        return [.. response.ReceivedMessages.Select(rm => (rm.Message, rm.AckId))];
    }

    public async Task AcknowledgeMessagesAsync(
        string subscriptionId,
        IEnumerable<string> ackIds,
        CancellationToken cancellationToken = default
    )
    {
        var subscriptionName = SubscriptionName.FromProjectSubscription(_projectId, subscriptionId);

        await _subscriberClient.AcknowledgeAsync(
            subscriptionName,
            ackIds,
            cancellationToken: cancellationToken
        );
    }

    public async Task PublishMessageAsync(
        string topicId,
        string messageText,
        CancellationToken cancellationToken = default
    )
    {
        var topicName = TopicName.FromProjectTopic(_projectId, topicId);

        var message = new PubsubMessage
        {
            Data = Google.Protobuf.ByteString.CopyFromUtf8(messageText),
        };

        await _publisherClient.PublishAsync(
            topicName,
            [message],
            cancellationToken: cancellationToken
        );
    }
}
