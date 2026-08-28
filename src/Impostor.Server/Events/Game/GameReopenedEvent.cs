using Impostor.Api.Events;
using Impostor.Api.Games;

namespace Impostor.Server.Events
{
    public class GameReopenedEvent : IGameReopenedEvent
    {
        public GameReopenedEvent(IGame game)
        {
            Game = game;
        }

        public IGame Game { get; }
    }
}
