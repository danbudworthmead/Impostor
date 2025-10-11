namespace Impostor.Api.Events.Player
{
    public interface IPlayerSabotageEvent : IPlayerEvent, IEventCancelable
    {
        bool IsCancelled { get; set; }
    }
}
