using System.IO.Pipes;
using Atlas.Core.Contracts;
using ProtoBuf;

namespace Atlas.App.Services;

public sealed class AtlasPipeClient(string pipeName = "AtlasFileIntelligenceV1")
{
    public async Task<TResponse> RoundTripAsync<TRequest, TResponse>(string messageType, TRequest payload, CancellationToken cancellationToken)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(cancellationToken);

        var envelope = new PipeEnvelope
        {
            MessageType = messageType,
            Payload = Serialize(payload)
        };

        Serializer.Serialize(client, envelope);
        await client.FlushAsync(cancellationToken);
        var response = Serializer.Deserialize<PipeEnvelope>(client);
        if (string.Equals(response.MessageType, "error", StringComparison.OrdinalIgnoreCase))
        {
            using var errorStream = new MemoryStream(response.Payload);
            var error = Serializer.Deserialize<ProgressEvent>(errorStream);
            throw new InvalidOperationException(error.Message);
        }

        using var responseStream = new MemoryStream(response.Payload);
        return Serializer.Deserialize<TResponse>(responseStream);
    }

    private static byte[] Serialize<T>(T payload)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, payload);
        return stream.ToArray();
    }
}
