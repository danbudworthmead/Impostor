using Impostor.Api.Innersloth;

namespace Impostor.Api.Events.Player
{
    /// <summary>
    /// Event that is called when a player calls a sabotage.
    /// It is cancellable.
    /// </summary>
    public interface IPlayerSabotagedEvent : IPlayerEvent, IEventCancelable
    {
        /// <summary>
        /// Gets the sabotage that was called.
        /// </summary>
        SystemTypes Type { get; }
    }
}
