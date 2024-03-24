using Impostor.Api.Events.Player;
using Impostor.Api.Games;
using Impostor.Api.Innersloth;
using Impostor.Api.Net;
using Impostor.Api.Net.Inner.Objects;

namespace Impostor.Server.Events.Player;

public class PlayerSabotagedEvent : IPlayerSabotagedEvent
{
    public PlayerSabotagedEvent(IGame game, IClientPlayer clientPlayer, IInnerPlayerControl playerControl, bool isCancelled, SystemTypes type)
    {
        Game = game;
        ClientPlayer = clientPlayer;
        PlayerControl = playerControl;
        IsCancelled = isCancelled;
        Type = type;
    }

    public IGame Game { get; }

    public IClientPlayer ClientPlayer { get; }

    public IInnerPlayerControl PlayerControl { get; }

    public bool IsCancelled { get; set; }

    public SystemTypes Type { get; }
}
