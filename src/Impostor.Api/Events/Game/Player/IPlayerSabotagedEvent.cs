using Impostor.Api.Innersloth;

namespace Impostor.Api.Events.Player;

public interface IPlayerSabotagedEvent : IPlayerEvent
{
    SystemTypes Type { get; }

    bool IsCancelled { get; set; }
}
