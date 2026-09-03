#nullable enable

using System.Net;
using System.Numerics;
using System.Threading.Tasks;
using Impostor.Api.Config;
using Impostor.Api.Events.Managers;
using Impostor.Api.Games;
using Impostor.Api.Games.Managers;
using Impostor.Api.Innersloth;
using Impostor.Api.Innersloth.GameOptions;
using Impostor.Api.Net;
using Impostor.Api.Net.Custom;
using Impostor.Api.Net.Manager;
using Impostor.Api.Utils;
using Impostor.Hazel;
using Impostor.Hazel.Abstractions;
using Impostor.Hazel.Extensions;
using Impostor.Server;
using Impostor.Server.Events;
using Impostor.Server.Net;
using Impostor.Server.Net.Custom;
using Impostor.Server.Net.Factories;
using Impostor.Server.Net.Inner;
using Impostor.Server.Net.Manager;
using Impostor.Server.Net.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Xunit;

namespace Impostor.Tests.Net
{
    /// <summary>
    /// Live testing of the meeting hud fix found extra, unnamed players in the vote list - more
    /// than there were real players. PopulateButtons walks GameNet.GameData.Players (every
    /// spawned InnerPlayerInfo), not the client roster, so this checks whether StartAsync's own
    /// RemoveFakePlayersAsync actually removes a fake player's PlayerInfo from that same
    /// collection, not just their character. A real player has to be present too - with only the
    /// fake one, removing it drops PlayerCount to 0 and destroys the game before the cleanup this
    /// checks even runs, which is not what happens in an actual lobby.
    /// </summary>
    public class RemoveFakePlayersAsyncTests
    {
        private static readonly GameVersion SupportedVersion = new(2026, 3, 18);

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
            public GameCode Create() => GameCode.From("FAKE01");
        }

        private sealed class TestOwnerClient : ClientBase
        {
            public TestOwnerClient()
                : base("owner", SupportedVersion, Language.English, QuickChatModes.FreeChatOrQuickChat, new PlatformSpecificData(Platforms.Unknown, "owner"), null)
            {
            }

            public override ValueTask HandleMessageAsync(IMessageReader message, MessageType messageType) => default;

            public override ValueTask HandleDisconnectAsync(string reason) => default;
        }

        private sealed class TestHazelConnection : IHazelConnection
        {
            public TestHazelConnection(IPEndPoint endPoint)
            {
                EndPoint = endPoint;
            }

            public IPEndPoint EndPoint { get; }

            public bool IsConnected => true;

            public IClient? Client { get; set; }

            public float AveragePing => 0f;

            public ValueTask SendAsync(IMessageWriter writer) => default;

            public ValueTask DisconnectAsync(string? reason, IMessageWriter? writer = null) => default;
        }

        private static async Task JoinBotAsync(Game game, ClientManager clientManager, ObjectPool<MessageReader> readerPool, string name, int port)
        {
            var connection = new TestHazelConnection(new IPEndPoint(IPAddress.Loopback, port));

            await clientManager.RegisterConnectionAsync(
                connection,
                name,
                SupportedVersion,
                Language.English,
                QuickChatModes.FreeChatOrQuickChat,
                new PlatformSpecificData(Platforms.Unknown, name));

            var client = (ClientBase)connection.Client!;
            var joinResult = await game.AddClientAsync(client);
            Assert.True(joinResult.IsSuccess, $"{name} failed to join: {joinResult.Error}");

            using var writer = MessageWriter.Get(MessageType.Reliable);
            writer.StartMessage(GameDataTag.SceneChangeFlag);
            writer.WritePacked(client.Id);
            writer.Write("OnlineGame");
            writer.EndMessage();

            var bytes = writer.ToByteArray(false);

            using var reader = readerPool.Get();
            reader.Update(bytes, 0, 0, bytes.Length, 0, null);

            var sender = (ClientPlayer)game.GetClientPlayer(client.Id)!;
            await game.HandleGameDataAsync(reader, sender, false);

            Assert.NotNull(client.Player!.Character);
        }

        [Fact]
        public async Task StartAsync_RemovesFakePlayerInfo_NotJustTheirCharacter()
        {
            await using var provider = BuildServices();
            var gameManager = provider.GetRequiredService<GameManager>();
            var clientManager = provider.GetRequiredService<ClientManager>();
            var readerPool = provider.GetRequiredService<ObjectPool<MessageReader>>();

            var game = (Game)(await gameManager.CreateAsync(new NormalGameOptions(), GameFilterOptions.CreateDefault()))!;
            await game.InitializeServerHostAsync(new TestOwnerClient());

            // A real player has to be here too - PlayerCount only counts these, and dropping to
            // zero when the fake one is removed would destroy the game before the cleanup below
            // ever runs, which is not what an actual lobby with real players in it does.
            await JoinBotAsync(game, clientManager, readerPool, "Real", 40901);

            var mascot = await game.SpawnFakePlayerAsync("Mascot", new Vector2(0f, -15f));
            Assert.NotNull(mascot);
            Assert.NotNull(mascot!.Character);

            var playerIdBeforeStart = mascot.Character!.PlayerId;
            Assert.NotNull(game.GameNet.GameData.GetPlayerById(playerIdBeforeStart));

            await game.StartAsync();
            Assert.Equal(GameStates.Started, game.GameState);

            // The character being gone is not enough - PopulateButtons (and anything else that
            // walks GameNet.GameData.Players rather than the client roster) would still see a
            // leftover PlayerInfo here and treat it as one more player in the round.
            Assert.Null(mascot.Character);
            Assert.Null(game.GameNet.GameData.GetPlayerById(playerIdBeforeStart));
        }

        [Fact]
        public async Task StartAsync_RemovesEveryFakePlayer_WhenThereAreSeveral()
        {
            await using var provider = BuildServices();
            var gameManager = provider.GetRequiredService<GameManager>();
            var clientManager = provider.GetRequiredService<ClientManager>();
            var readerPool = provider.GetRequiredService<ObjectPool<MessageReader>>();

            var game = (Game)(await gameManager.CreateAsync(new NormalGameOptions(), GameFilterOptions.CreateDefault()))!;
            await game.InitializeServerHostAsync(new TestOwnerClient());

            await JoinBotAsync(game, clientManager, readerPool, "Real", 40911);

            // Mirrors an actual lobby: the mascot and the game mode label (LobbyDecorationManager)
            // plus the start pad (StartPadManager) are three separate fake players.
            var mascot = await game.SpawnFakePlayerAsync("Mascot", new Vector2(0f, -15f));
            var modeLabel = await game.SpawnFakePlayerAsync("Game Mode: Standard", new Vector2(0f, 0f));
            var startPad = await game.SpawnFakePlayerAsync("Host stand here to start", new Vector2(0f, -15f));

            Assert.NotNull(mascot);
            Assert.NotNull(modeLabel);
            Assert.NotNull(startPad);

            var playerIds = new[] { mascot!.Character!.PlayerId, modeLabel!.Character!.PlayerId, startPad!.Character!.PlayerId };
            Assert.All(playerIds, id => Assert.NotNull(game.GameNet.GameData.GetPlayerById(id)));

            await game.StartAsync();
            Assert.Equal(GameStates.Started, game.GameState);

            Assert.All(playerIds, id => Assert.Null(game.GameNet.GameData.GetPlayerById(id)));
        }
    }
}
