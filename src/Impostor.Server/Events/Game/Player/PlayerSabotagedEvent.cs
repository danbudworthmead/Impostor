using Impostor.Api.Events.Player;
using Impostor.Api.Games;
using Impostor.Api.Innersloth;
using Impostor.Api.Net;
using Impostor.Api.Net.Inner.Objects;

namespace Impostor.Server.Events.Player
{
    public class PlayerSabotagedEvent : IPlayerSabotagedEvent
    {
        public PlayerSabotagedEvent(IGame game, SystemTypes type)
        {
            Type = type;
            Game = game;
        }

        public IGame Game { get; }

        public IClientPlayer ClientPlayer { get; }

        public IInnerPlayerControl PlayerControl { get; }

        public SystemTypes Type { get; private set; }

        public bool IsCancelled { get; set; } = false;
    }
}
