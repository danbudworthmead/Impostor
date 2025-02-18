using Impostor.Api.Events.Client;
using Impostor.Api.Net;

namespace Impostor.Server.Events.Client;

public class ClientAddVoteEvent : IClientAddVoteEvent
{
    public ClientAddVoteEvent(IClient client, int clientId, int targetClientId, IHazelConnection connection)
    {
        Client = client;
        ClientId = clientId;
        TargetClientId = targetClientId;
        Connection = connection;
    }

    public IHazelConnection Connection { get; }

    public IClient Client { get; }

    public int ClientId { get; }

    public int TargetClientId { get; }
}
