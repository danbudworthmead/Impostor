using Impostor.Api.Games;
using Impostor.Api.Innersloth;

namespace Impostor.Api.Events.Player
{
    public interface IPlayerSabotagedEvent : IEventCancelable
    {
        SystemTypes Type { get; }

        public IGame Game { get; }
    }
}
