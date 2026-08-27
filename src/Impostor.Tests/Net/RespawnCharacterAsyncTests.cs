using System;
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
using Microsoft.Extensions.Options;
using Xunit;

namespace Impostor.Tests.Net
{
    /// <summary>
    /// Exercises RespawnCharacterAsync against a real Game/GameManager/ClientManager, wired up
    /// through DI the same way the server itself does, but with virtual (connectionless) players
    /// standing in for real ones - there is no network here, only the server-side bookkeeping a
    /// respawn is responsible for. It cannot see what a real client does with the messages this
    /// produces: the kill-animation race and the PlayerId shadowing bug that motivated this test
    /// were both client-side reactions no server-only test could have caught. What it can catch is
    /// regressions in the contract RespawnCharacterAsync itself owns - a new character, the old
    /// one's PlayerId cleared, position carried over - the same way every prior fix in this area
    /// was reasoned about from source before ever reaching a live client.
    /// </summary>
    public class RespawnCharacterAsyncTests
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

        /// <summary>
        /// A minimal stand-in for the connectionless "owner" GameManager.CreateAsync would
        /// normally take from a real client's HostGame request - only ever used to satisfy
        /// InitializeServerHostAsync's signature, never itself added as a player.
        /// </summary>
        private sealed class TestOwnerClient : ClientBase
        {
            public TestOwnerClient()
                : base("owner", default, Impostor.Api.Innersloth.Language.English, Impostor.Api.Innersloth.QuickChatModes.FreeChatOrQuickChat, new Impostor.Api.Innersloth.PlatformSpecificData(Impostor.Api.Innersloth.Platforms.Unknown, "owner"), null)
            {
            }

            public override ValueTask HandleMessageAsync(IMessageReader message, MessageType messageType) => default;

            public override ValueTask HandleDisconnectAsync(string reason) => default;
        }

        [Fact]
        public async Task RespawnCharacterAsync_ReplacesCharacter_ClearsOldPlayerId_KeepsPosition()
        {
            await using var provider = BuildServices();

            var gameManager = provider.GetRequiredService<GameManager>();

            var game = (Game)(await gameManager.CreateAsync(
                new NormalGameOptions(),
                GameFilterOptions.CreateDefault()))!;

            await game.InitializeServerHostAsync(new TestOwnerClient());

            var victim = await game.SpawnFakePlayerAsync("Victim", new Vector2(3f, 4f));
            Assert.NotNull(victim);

            var oldCharacter = victim!.Character;
            Assert.NotNull(oldCharacter);
            var oldNetId = oldCharacter!.NetId;

            var newCharacter = await game.RespawnCharacterAsync(victim);

            Assert.NotNull(newCharacter);
            Assert.NotEqual(oldNetId, newCharacter!.NetId);
            Assert.Equal(new Vector2(3f, 4f), newCharacter.NetworkTransform.Position);
            Assert.Same(newCharacter, victim.Character);

            // The old character is not just replaced - a client keeps it in its own
            // AllPlayerControls until it independently destroys it, and resolves "who is
            // PlayerId N" by scanning that list. Clearing PlayerId is what keeps the stale
            // entry from shadowing the new one; see RespawnCharacterAsync's own remarks.
            Assert.Equal(byte.MaxValue, oldCharacter.PlayerId);
        }
    }
}
