namespace Impostor.Api.Events
{
    /// <summary>
    ///     Called when a game that had ended becomes joinable again, ahead of a new round -
    ///     the same lobby, not a new one, which is what tells this apart from
    ///     <see cref="IGameCreatedEvent" />.
    /// </summary>
    /// <remarks>
    ///     A fresh game's lobby only ever needs decorating once, on creation - <see cref="IGameCreatedEvent" />
    ///     covers that. A game returning to its lobby after a round has ended needs the exact
    ///     same decorating again: anything spawned only at creation (a start pad standing in for
    ///     a server-hosted game's missing host, a mascot, anything else that does not survive
    ///     <c>RemoveFakePlayersAsync</c> starting the round it was in) has to be put back, since
    ///     nothing else will ever ask for it again.
    /// </remarks>
    public interface IGameReopenedEvent : IGameEvent
    {
    }
}
