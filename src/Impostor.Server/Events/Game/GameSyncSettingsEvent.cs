using Impostor.Api.Events;
using Impostor.Api.Games;

namespace Impostor.Server.Events
{
    public class GameSyncSettingsEvent : IGameStartingEvent
    {
        public GameSyncSettingsEvent(IGame game)
        {
            Game = game;
        }

        public IGame Game { get; }
    }
}
