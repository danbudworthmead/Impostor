using Impostor.Api.Net;

namespace Impostor.Api.Events.Client
{
    /// <summary>
    ///     Called just after a <see cref="IClient"/> sends AddVote.
    /// </summary>
    public interface IClientAddVoteEvent : IClientEvent
    {
        public IClient Client { get; }

        public int ClientId { get; }

        public int TargetClientId { get; }
    }
}
