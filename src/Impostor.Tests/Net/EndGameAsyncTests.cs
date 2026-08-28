using System.Numerics;
using System.Threading.Tasks;
using Impostor.Api.Config;
using Impostor.Api.Events;
using Impostor.Api.Events.Managers;
using Impostor.Api.Games;
using Impostor.Api.Games.Managers;
using Impostor.Api.Innersloth;
using Impostor.Api.Innersloth.GameOptions;
using Impostor.Api.Net;
using Impostor.Api.Net.Custom;
using Impostor.Api.Net.Manager;
using Impostor.Api.Utils;
using Impostor.Hazel.Abstractions;
using Impostor.Hazel.Extensions;
using Impostor.Server;
using Impostor.Server.Events;
using Impostor.Server.Net;
using Impostor.Server.Net.Custom;
using Impostor.Server.Net.Factories;
using Impostor.Server.Net.Manager;
using Impostor.Server.Net.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Impostor.Tests.Net
{
    /// <summary>
    /// Exercises IGame.EndGameAsync, added after a server-hosted Zombies round would end for
    /// players (the plugin's own EndGame message reached every client) but the server's own
    /// GameState stayed Started forever - so "Play Again" tried to rejoin a lobby the server
    /// still considered mid-round and rejected everyone as joining a game that already started.
    /// The plugin's per-player EndGame helper calls this once per recipient, so the case that
    /// actually caused that bug - calling it more than once - is the one this guards.
    /// </summary>
    public class EndGameAsyncTests
    {
        private static ServiceProvider BuildServices()
        {
            var services = new ServiceCollection();

            services.AddLogging(builder => builder.AddDebug());

            services.AddSingleton<IDateTimeProvider, Impostor.Server.Utils.RealDateTimeProvider>();
            services.AddSingleton<ClientIdentityCache>();

            services.Configure<ServerConfig>(_ => { });
            services.Configure<CompatibilityConfig>(_ => { });
            services.Configure<TimeoutConfig>(_ => { });
            services.Configure<AntiCheatConfig>(_ => { });

            services.AddSingleton<GameManager>();
            services.AddSingleton<IGameManager>(p => p.GetRequiredService<GameManager>());

            services.AddSingleton<IGameCodeFactory, TestGameCodeFactory>();

            services.AddSingleton<ClientManager>();
            services.AddSingleton<IClientFactory, ClientFactory<Client>>();
            services.AddSingleton<IEventManager, EventManager>();
            services.AddSingleton<ICompatibilityManager, CompatibilityManager>();

            services.AddEventPools();
            services.AddHazel();
            services.AddSingleton<ICustomMessageManager<ICustomRootMessage>, CustomMessageManager<ICustomRootMessage>>();
            services.AddSingleton<ICustomMessageManager<ICustomRpc>, CustomMessageManager<ICustomRpc>>();

            return services.BuildServiceProvider();
        }

        private sealed class TestGameCodeFactory : IGameCodeFactory
        {
            public GameCode Create() => GameCode.From("TEST12");
        }

        private sealed class TestOwnerClient : ClientBase
        {
            public TestOwnerClient()
                : base("owner", default, Language.English, QuickChatModes.FreeChatOrQuickChat, new PlatformSpecificData(Platforms.Unknown, "owner"), null)
            {
            }

            public override ValueTask HandleMessageAsync(IMessageReader message, MessageType messageType) => default;

            public override ValueTask HandleDisconnectAsync(string reason) => default;
        }

        private sealed class GameEndedListener : IEventListener
        {
            public int CallCount { get; private set; }

            public GameOverReason? LastReason { get; private set; }

            [EventListener]
            public void OnGameEnded(IGameEndedEvent e)
            {
                CallCount++;
                LastReason = e.GameOverReason;
            }
        }

        [Fact]
        public async Task EndGameAsync_TransitionsGameState_AndIgnoresRepeatCalls()
        {
            await using var provider = BuildServices();

            var gameManager = provider.GetRequiredService<GameManager>();
            var eventManager = provider.GetRequiredService<IEventManager>();

            var listener = new GameEndedListener();
            using var registration = eventManager.RegisterListener(listener);

            var game = (Game)(await gameManager.CreateAsync(
                new NormalGameOptions(),
                GameFilterOptions.CreateDefault()))!;

            await game.InitializeServerHostAsync(new TestOwnerClient());

            var player = await game.SpawnFakePlayerAsync("Player", new Vector2(1f, 1f));
            Assert.NotNull(player!.Character);

            await game.StartAsync();

            Assert.Equal(GameStates.Started, game.GameState);
            Assert.NotNull(game.GameNet.ShipStatus);

            await game.EndGameAsync(GameOverReason.ImpostorsByKill);

            Assert.Equal(GameStates.Ended, game.GameState);
            Assert.Equal(1, listener.CallCount);
            Assert.Equal(GameOverReason.ImpostorsByKill, listener.LastReason);

            // The map itself: a real host despawns this on its own initiative when a round ends,
            // and Impostor just relays that. A server-hosted game has no host client to do it, so
            // this has to take the map down itself - see DespawnShipStatusAsync's remarks for the
            // symptom leaving it spawned in produced (a rejoin drawing the lobby over a map that
            // never actually left).
            Assert.Null(game.GameNet.ShipStatus);

            // Every player's character too - left in place, SpawnPlayerControlAsync's own "already
            // has one" guard would make the next round reuse it, PlayerId and all, against a fresh
            // PlayerInfo that was never guaranteed to carry the same one.
            Assert.Null(player.Character);

            // The plugin calls this once per player it sends its own EndGame message to, since
            // the wire protocol has no single broadcast for "different reason per recipient". A
            // repeat call must not re-run the transition or fire the event a second time.
            await game.EndGameAsync(GameOverReason.CrewmatesByTask);

            Assert.Equal(GameStates.Ended, game.GameState);
            Assert.Equal(1, listener.CallCount);
            Assert.Equal(GameOverReason.ImpostorsByKill, listener.LastReason);
        }
    }
}
