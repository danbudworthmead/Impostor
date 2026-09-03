#nullable enable

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Impostor.Api.Config;
using Impostor.Api.Events;
using Impostor.Api.Events.Managers;
using Impostor.Api.Events.Player;
using Impostor.Api.Games;
using Impostor.Api.Games.Managers;
using Impostor.Api.Innersloth;
using Impostor.Api.Innersloth.GameOptions;
using Impostor.Api.Net;
using Impostor.Api.Net.Custom;
using Impostor.Api.Net.Inner;
using Impostor.Api.Net.Inner.Objects;
using Impostor.Api.Net.Manager;
using Impostor.Api.Net.Messages.Rpcs;
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
using Impostor.Server.Net.Inner.Objects;
using Impostor.Server.Net.Manager;
using Impostor.Server.Net.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Xunit;

namespace Impostor.Tests.Net
{
    /// <summary>
    /// A server-hosted game has no host client, and reporting a body or calling an emergency
    /// meeting is entirely host-driven in vanilla Among Us: the host's own client is what
    /// notices ReportDeadBody and spawns the meeting hud, and what tells everyone the vote is in
    /// and the meeting is closing once it has resolved. Nothing was watching for either under
    /// SAAH, so a report did nothing at all. These exercise the server acting as its own host for
    /// both halves: starting a meeting from a real ReportDeadBody RPC, and a real vote resolving
    /// all the way through to the exiled player actually being marked dead.
    /// </summary>
    public class MeetingHudTests
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

        private static readonly GameVersion SupportedVersion = new(2026, 3, 18);

        private sealed class TestGameCodeFactory : IGameCodeFactory
        {
            private int _next;

            public GameCode Create() => GameCode.From($"MTG{_next++:D3}");
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

        private static async Task<ClientPlayer> JoinBotAsync(Game game, ClientManager clientManager, ObjectPool<MessageReader> readerPool, string name, int port)
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

            await SendSubMessageAsync(game, readerPool, (ClientPlayer)game.GetClientPlayer(client.Id)!, writer =>
            {
                writer.StartMessage(GameDataTag.SceneChangeFlag);
                writer.WritePacked(client.Id);
                writer.Write("OnlineGame");
                writer.EndMessage();
            });

            var player = (ClientPlayer)client.Player!;
            Assert.NotNull(player.Character);

            return player;
        }

        /// <summary>
        /// Builds one GameData sub-message (the same StartMessage(tag)/EndMessage framing every
        /// real client's own outgoing message uses) and feeds it straight into
        /// HandleGameDataAsync, the same entry point a real connection's incoming bytes reach.
        /// </summary>
        private static async Task SendSubMessageAsync(Game game, ObjectPool<MessageReader> readerPool, ClientPlayer sender, Action<IMessageWriter> writeSubMessage, bool toPlayer = false)
        {
            using var writer = MessageWriter.Get(MessageType.Reliable);
            writeSubMessage(writer);

            var bytes = writer.ToByteArray(false);

            using var reader = readerPool.Get();
            reader.Update(bytes, 0, 0, bytes.Length, 0, null);

            await game.HandleGameDataAsync(reader, sender, toPlayer);
        }

        private static Task ReportDeadBodyAsync(Game game, ObjectPool<MessageReader> readerPool, ClientPlayer reporter, byte targetId = byte.MaxValue)
        {
            return SendSubMessageAsync(game, readerPool, reporter, writer =>
            {
                writer.StartMessage(GameDataTag.RpcFlag);
                writer.WritePacked(reporter.Character!.NetId);
                writer.Write((byte)RpcCalls.ReportDeadBody);
                Rpc11ReportDeadBody.Serialize(writer, targetId);
                writer.EndMessage();
            });
        }

        private static Task CastVoteAsync(Game game, ObjectPool<MessageReader> readerPool, uint meetingHudNetId, ClientPlayer voter, byte suspectPlayerId)
        {
            // CastVote from a non-host player is a "cmd" addressed at the host - ValidateCmd
            // requires the GameDataTo target to actually resolve to one, which the virtual host
            // seat still does under SAAH even with no character of its own.
            return SendSubMessageAsync(
                game,
                readerPool,
                voter,
                writer =>
                {
                    writer.WritePacked(game.HostId);
                    writer.StartMessage(GameDataTag.RpcFlag);
                    writer.WritePacked(meetingHudNetId);
                    writer.Write((byte)RpcCalls.CastVote);
                    Rpc24CastVote.Serialize(writer, voter.Character!.PlayerId, (sbyte)suspectPlayerId);
                    writer.EndMessage();
                },
                toPlayer: true);
        }

        [Fact]
        public async Task ReportDeadBody_ServerHosted_SpawnsAMeetingHud()
        {
            await using var provider = BuildServices();
            var gameManager = provider.GetRequiredService<GameManager>();
            var clientManager = provider.GetRequiredService<ClientManager>();
            var readerPool = provider.GetRequiredService<ObjectPool<MessageReader>>();

            var game = (Game)(await gameManager.CreateAsync(new NormalGameOptions(), GameFilterOptions.CreateDefault()))!;
            await game.InitializeServerHostAsync(new TestOwnerClient());

            var first = await JoinBotAsync(game, clientManager, readerPool, "First", 40001);
            await JoinBotAsync(game, clientManager, readerPool, "Second", 40002);

            await game.StartAsync();
            Assert.Equal(GameStates.Started, game.GameState);
            Assert.Null(game.GameNet.MeetingHud);

            // No specific body - the emergency button uses the same RPC with this sentinel.
            await ReportDeadBodyAsync(game, readerPool, first);

            Assert.NotNull(game.GameNet.MeetingHud);
            Assert.Null(game.GameNet.MeetingHud!.Reporter);
        }

        private sealed class StartMeetingListener : IEventListener
        {
            public int CallCount { get; private set; }

            public IClientPlayer? ClientPlayer { get; private set; }

            public IInnerPlayerControl? Body { get; private set; }

            [EventListener]
            public void OnStartMeeting(IPlayerStartMeetingEvent e)
            {
                CallCount++;
                ClientPlayer = e.ClientPlayer;
                Body = e.Body;
            }
        }

        [Fact]
        public async Task ReportDeadBody_ServerHosted_BroadcastsStartMeeting()
        {
            await using var provider = BuildServices();
            var gameManager = provider.GetRequiredService<GameManager>();
            var clientManager = provider.GetRequiredService<ClientManager>();
            var readerPool = provider.GetRequiredService<ObjectPool<MessageReader>>();
            var eventManager = provider.GetRequiredService<IEventManager>();

            var listener = new StartMeetingListener();
            using var registration = eventManager.RegisterListener(listener);

            var game = (Game)(await gameManager.CreateAsync(new NormalGameOptions(), GameFilterOptions.CreateDefault()))!;
            await game.InitializeServerHostAsync(new TestOwnerClient());

            var first = await JoinBotAsync(game, clientManager, readerPool, "First", 40051);
            var second = await JoinBotAsync(game, clientManager, readerPool, "Second", 40052);

            await game.StartAsync();

            // The meeting hud object spawn only ever carried vote state - StartMeeting is the
            // separate signal a host client also sends to actually cue every client's meeting
            // screen open, which the stuck-screen report this test class exists for turned out
            // to be missing entirely.
            await ReportDeadBodyAsync(game, readerPool, first, second.Character!.PlayerId);

            Assert.Equal(1, listener.CallCount);
            Assert.Equal(first.Client.Id, listener.ClientPlayer!.Client.Id);
            Assert.Equal(second.Character!.PlayerId, listener.Body!.PlayerId);
        }

        [Fact]
        public async Task ReportDeadBody_WithABody_RecordsTheReporter()
        {
            await using var provider = BuildServices();
            var gameManager = provider.GetRequiredService<GameManager>();
            var clientManager = provider.GetRequiredService<ClientManager>();
            var readerPool = provider.GetRequiredService<ObjectPool<MessageReader>>();

            var game = (Game)(await gameManager.CreateAsync(new NormalGameOptions(), GameFilterOptions.CreateDefault()))!;
            await game.InitializeServerHostAsync(new TestOwnerClient());

            var first = await JoinBotAsync(game, clientManager, readerPool, "First", 40101);
            var second = await JoinBotAsync(game, clientManager, readerPool, "Second", 40102);

            await game.StartAsync();

            await ReportDeadBodyAsync(game, readerPool, first, second.Character!.PlayerId);

            Assert.NotNull(game.GameNet.MeetingHud);
            Assert.Equal(second.Character!.PlayerId, game.GameNet.MeetingHud!.Reporter?.PlayerId);

            // Not just the server's own bookkeeping - DidReport is what actually goes out over
            // the wire in the vote area list, which is what the client reads to know who found
            // the body at all. Missing this was the actual cause of the meeting screen this
            // whole test class exists for getting stuck with nothing to show.
            var reporterState = ((IInnerMeetingHud)game.GameNet.MeetingHud!).PlayerStates
                .Single(state => ((InnerPlayerInfo)state.TargetPlayer).PlayerId == second.Character!.PlayerId);
            Assert.True(reporterState.DidReport);

            var everyoneElse = ((IInnerMeetingHud)game.GameNet.MeetingHud!).PlayerStates
                .Where(state => ((InnerPlayerInfo)state.TargetPlayer).PlayerId != second.Character!.PlayerId);
            Assert.All(everyoneElse, state => Assert.False(state.DidReport));
        }

        [Fact]
        public async Task ReportDeadBody_WhileOneIsAlreadyActive_DoesNotSpawnASecond()
        {
            await using var provider = BuildServices();
            var gameManager = provider.GetRequiredService<GameManager>();
            var clientManager = provider.GetRequiredService<ClientManager>();
            var readerPool = provider.GetRequiredService<ObjectPool<MessageReader>>();

            var game = (Game)(await gameManager.CreateAsync(new NormalGameOptions(), GameFilterOptions.CreateDefault()))!;
            await game.InitializeServerHostAsync(new TestOwnerClient());

            var first = await JoinBotAsync(game, clientManager, readerPool, "First", 40201);
            await JoinBotAsync(game, clientManager, readerPool, "Second", 40202);

            await game.StartAsync();

            await ReportDeadBodyAsync(game, readerPool, first);
            var firstHud = game.GameNet.MeetingHud;
            Assert.NotNull(firstHud);

            await ReportDeadBodyAsync(game, readerPool, first);

            Assert.Same(firstHud, game.GameNet.MeetingHud);
        }

        [Fact]
        public async Task Vote_ServerHosted_ExilesTheVotedOutPlayerForReal()
        {
            await using var provider = BuildServices();
            var gameManager = provider.GetRequiredService<GameManager>();
            var clientManager = provider.GetRequiredService<ClientManager>();
            var readerPool = provider.GetRequiredService<ObjectPool<MessageReader>>();

            var game = (Game)(await gameManager.CreateAsync(new NormalGameOptions(), GameFilterOptions.CreateDefault()))!;
            await game.InitializeServerHostAsync(new TestOwnerClient());

            var first = await JoinBotAsync(game, clientManager, readerPool, "First", 40301);
            var second = await JoinBotAsync(game, clientManager, readerPool, "Second", 40302);
            var third = await JoinBotAsync(game, clientManager, readerPool, "Third", 40303);

            await game.StartAsync();

            await ReportDeadBodyAsync(game, readerPool, first);
            var meetingHud = game.GameNet.MeetingHud;
            Assert.NotNull(meetingHud);

            var meetingHudNetId = meetingHud!.NetId;

            // Every real player votes for Third - once everyone still able to vote has, the
            // meeting hud's own built-in check resolves the vote immediately rather than waiting
            // out its timer.
            await CastVoteAsync(game, readerPool, meetingHudNetId, first, third.Character!.PlayerId);
            await CastVoteAsync(game, readerPool, meetingHudNetId, second, third.Character!.PlayerId);
            await CastVoteAsync(game, readerPool, meetingHudNetId, third, third.Character!.PlayerId);

            // A host client is what normally turns "the vote resolved" into "the exiled player is
            // actually dead" once it has shown everyone the result - the point of this whole
            // feature is that the server does that directly now instead of nothing happening.
            Assert.True(third.Character!.PlayerInfo!.IsDead);
            Assert.Equal(DeathReason.Exile, third.Character!.PlayerInfo!.LastDeathReason);

            // And the meeting itself is torn down so a later report can start a fresh one.
            Assert.Null(game.GameNet.MeetingHud);
        }

        [Fact]
        public async Task AfterAMeetingResolves_ANewReportCanStartAnother()
        {
            await using var provider = BuildServices();
            var gameManager = provider.GetRequiredService<GameManager>();
            var clientManager = provider.GetRequiredService<ClientManager>();
            var readerPool = provider.GetRequiredService<ObjectPool<MessageReader>>();

            var game = (Game)(await gameManager.CreateAsync(new NormalGameOptions(), GameFilterOptions.CreateDefault()))!;
            await game.InitializeServerHostAsync(new TestOwnerClient());

            var first = await JoinBotAsync(game, clientManager, readerPool, "First", 40401);
            var second = await JoinBotAsync(game, clientManager, readerPool, "Second", 40402);

            await game.StartAsync();

            await ReportDeadBodyAsync(game, readerPool, first);
            var firstHud = game.GameNet.MeetingHud;
            Assert.NotNull(firstHud);

            var firstHudNetId = firstHud!.NetId;

            // Skip both votes (253 - VoteType.Skipped, not 255/HasNotVoted, which would not
            // count as having voted at all) so this resolves as a tie rather than exiling
            // anyone - only whether a fresh meeting hud can be spawned afterwards is under test
            // here.
            const byte skip = 253;
            await CastVoteAsync(game, readerPool, firstHudNetId, first, skip);
            await CastVoteAsync(game, readerPool, firstHudNetId, second, skip);

            Assert.Null(game.GameNet.MeetingHud);

            await ReportDeadBodyAsync(game, readerPool, second);

            Assert.NotNull(game.GameNet.MeetingHud);
            Assert.NotSame(firstHud, game.GameNet.MeetingHud);
        }
    }
}
