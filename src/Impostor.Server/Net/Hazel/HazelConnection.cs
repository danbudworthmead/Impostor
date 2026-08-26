using System.Net;
using System.Threading.Tasks;
using Impostor.Api;
using Impostor.Api.Config;
using Impostor.Api.Net;
using Impostor.Hazel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Impostor.Server.Net.Hazel
{
    internal class HazelConnection : IHazelConnection
    {
        private readonly ILogger<HazelConnection> _logger;
        private readonly AntiCheatConfig _antiCheatConfig;

        public HazelConnection(Connection innerConnection, ILogger<HazelConnection> logger, IOptions<AntiCheatConfig> antiCheatOptions)
        {
            _logger = logger;
            _antiCheatConfig = antiCheatOptions.Value;
            InnerConnection = innerConnection;
            innerConnection.DataReceived = ConnectionOnDataReceived;
            innerConnection.Disconnected = ConnectionOnDisconnected;
        }

        public Connection InnerConnection { get; }

        public IPEndPoint EndPoint => InnerConnection.EndPoint;

        public bool IsConnected => InnerConnection.State == ConnectionState.Connected;

        public IClient? Client { get; set; }

        public float AveragePing => InnerConnection is NetworkConnection networkConnection ? networkConnection.AveragePingMs : 0;

        public ValueTask SendAsync(IMessageWriter writer)
        {
            return InnerConnection.SendAsync(writer);
        }

        public ValueTask DisconnectAsync(string? reason, IMessageWriter? writer = null)
        {
            return InnerConnection.Disconnect(reason, writer as MessageWriter);
        }

        public void DisposeInnerConnection()
        {
            InnerConnection.Dispose();
        }

        private async ValueTask ConnectionOnDisconnected(DisconnectedEventArgs e)
        {
            if (Client != null)
            {
                await Client.HandleDisconnectAsync(e.Reason);
            }
        }

        private async ValueTask ConnectionOnDataReceived(DataReceivedEventArgs e)
        {
            if (Client == null)
            {
                return;
            }

            // Check raw message size against the configured limit.
            // Innersloth requires full packet ≤ 1200 bytes (1168 bytes payload after 32 bytes IP+UDP headers).
            if (e.Message.Length > _antiCheatConfig.PacketSizeLimit)
            {
                if (await Client.ReportCheatAsync(
                        new CheatContext("RootMessage"),
                        CheatCategory.PacketSize,
                        $"Received a message that is too large, length: {e.Message.Length}"))
                {
                    return;
                }
            }

            while (true)
            {
                if (e.Message.Position >= e.Message.Length)
                {
                    break;
                }

                using (var message = e.Message.ReadMessage())
                {
                    try
                    {
                        await Client.HandleMessageAsync(message, e.Type);
                    }
                    catch (System.Exception ex)
                    {
                        // An exception escaping here used to unwind into Hazel's receive loop,
                        // which stops serving this connection without a word: the client simply
                        // sees the server go quiet and times out. Losing one message is bad, but
                        // losing the connection silently is much worse to diagnose.
                        _logger.LogError(
                            ex,
                            "Exception handling a {Tag} message from client {ClientId}, dropping it.",
                            message.Tag,
                            Client.Id);
                    }
                }

                if (!IsConnected)
                {
                    break;
                }
            }
        }
    }
}
