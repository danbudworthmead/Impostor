using System.Linq;
using System.Threading.Tasks;
using Impostor.Api;
using Impostor.Api.Innersloth;
using Impostor.Api.Net;
using Impostor.Hazel;
using Impostor.Server.Events;
using Impostor.Server.Net.Hazel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Impostor.Server.Net.State
{
    internal partial class Game
    {
        /// <summary>
        ///     Puts a server-controlled client into the host seat before any real player joins,
        ///     so that <see cref="PlayerAdd" /> assigns it the host id naturally. There is no
        ///     packet for handing the host role over mid-lobby, so claiming it first is the only
        ///     way to do this without faking a player disconnect.
        /// </summary>
        /// <param name="owner">The client that asked for this game to be created.</param>
        /// <returns>A task that completes once the server holds the host seat.</returns>
        internal async ValueTask InitializeServerHostAsync(IClient owner)
        {
            if (ServerHost != null || HostId != -1)
            {
                return;
            }

            // Adopt the creating player's version. Joining clients are checked against the host's
            // version, so a synthetic one here would lock everybody out of the lobby.
            var client = new ServerHostClient(_clientManager.NextId(), owner.GameVersion);
            var player = new ClientPlayer(
                _serviceProvider.GetRequiredService<ILogger<ClientPlayer>>(),
                client,
                this,
                _timeoutConfig.SpawnTimeout);

            // It never spawns a character, so the spawn timeout must not be allowed to kick it.
            player.DisableSpawnTimeout();
            player.Limbo = LimboStates.NotLimbo;
            client.Player = player;

            ServerHost = player;

            await PlayerAdd(player);

            _logger.LogInformation("{Code} - Server took the host seat (client id {ClientId}).", Code, client.Id);
        }

        private async ValueTask PlayerAdd(ClientPlayer player)
        {
            // Store player.
            if (!_players.TryAdd(player.Client.Id, player))
            {
                throw new ImpostorException("Failed to add player to game.");
            }

            // Assign hostId if none is set.
            if (HostId == -1)
            {
                HostId = player.Client.Id;
            }

            await _eventManager.CallAsync(new GamePlayerJoinedEvent(this, player));
        }

        private async ValueTask<bool> PlayerRemove(int playerId, bool isBan = false)
        {
            if (!_players.TryRemove(playerId, out var player))
            {
                return false;
            }

            _logger.LogInformation("{0} - Player {1} ({2}) has left.", Code, player.Client.Name, playerId);

            if (GameState == GameStates.Starting || GameState == GameStates.Started || GameState == GameStates.NotStarted)
            {
                if (player.Character?.PlayerInfo != null)
                {
                    player.Character.PlayerInfo.Disconnected = true;
                    player.Character.PlayerInfo.LastDeathReason = DeathReason.Disconnect;
                }
            }

            player.Client.Player = null;

            // Host migration.
            if (HostId == playerId)
            {
                await MigrateHost();
                await _eventManager.CallAsync(new GameHostChangedEvent(this, player, Host));
            }

            // Game is empty, remove it. The server host never leaves, so it would otherwise keep
            // an abandoned game alive forever: count real players instead.
            if (PlayerCount == 0 || Host == null)
            {
                GameState = GameStates.Destroyed;

                // Remove instance reference.
                await _gameManager.RemoveAsync(Code);
                return true;
            }

            if (isBan && player.Client.Connection != null)
            {
                BanIp(player.Client.Connection.EndPoint.Address);
            }

            await _eventManager.CallAsync(new GamePlayerLeftEvent(this, player, isBan));

            // Player can refuse to be kicked and keep the connection open, check for this.
            _ = Task.Run(async () =>
            {
                await Task.Delay(_timeoutConfig.ConnectionTimeout);

                if (player.Client.Connection is { IsConnected: true } and HazelConnection hazel)
                {
                    _logger.LogInformation("{0} - Player {1} ({2}) kept connection open after leaving, disposing.", Code, player.Client.Name, playerId);
                    await player.Client.DisconnectAsync(isBan ? DisconnectReason.Banned : DisconnectReason.Kicked);
                }
            });

            // Clean up the PlayerInfo if we own it and we're still in the lobby
            if (GameState == GameStates.NotStarted)
            {
                if (GameNet.GameData.PlayersByClientId.TryGetValue(playerId, out var playerInfo))
                {
                    await DespawnPlayerInfoAsync(playerInfo);
                }
            }

            return true;
        }

        private async ValueTask MigrateHost()
        {
            // In a server hosted game the host seat belongs to the server for the lifetime of
            // the game; a real player must never inherit it.
            if (ServerHost != null)
            {
                HostId = ServerHost.Client.Id;
                return;
            }

            // Pick the first player as new host.
            var host = _players
                .Select(p => p.Value)
                .FirstOrDefault();

            if (host == null)
            {
                return;
            }

            foreach (var player in _players.Values)
            {
                player.Character?.RequestedPlayerName.Clear();
                player.Character?.RequestedColorId.Clear();
            }

            HostId = host.Client.Id;
            _logger.LogInformation("{0} - Assigned {1} ({2}) as new host.", Code, host.Client.Name, host.Client.Id);

            // Check our current game state.
            if (GameState == GameStates.Ended && host.Limbo == LimboStates.WaitingForHost)
            {
                GameState = GameStates.NotStarted;

                // Spawn the host.
                await HandleJoinGameNew(host, false);

                // Pull players out of limbo.
                await CheckLimboPlayers();
            }
        }

        private async ValueTask CheckLimboPlayers()
        {
            using var message = MessageWriter.Get(MessageType.Reliable);

            foreach (var (_, player) in _players.Where(x => x.Value.Limbo == LimboStates.WaitingForHost))
            {
                WriteJoinedGameMessage(message, true, player);
                WriteAlterGameMessage(message, false, IsPublic);

                player.Limbo = LimboStates.NotLimbo;

                await SendToAsync(message, player.Client.Id);
            }
        }
    }
}
