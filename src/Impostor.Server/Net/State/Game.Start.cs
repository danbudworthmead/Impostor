using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Impostor.Api.Innersloth;
using Impostor.Api.Innersloth.GameOptions;
using Impostor.Api.Innersloth.Maps;
using Impostor.Api.Net.Inner;
using Impostor.Api.Net.Messages;
using Impostor.Api.Net.Messages.Rpcs;
using Impostor.Hazel;
using Impostor.Server.Events;
using Impostor.Server.Net.Inner;
using Impostor.Server.Net.Inner.Objects.ShipStatus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Impostor.Server.Net.State
{
    /// <summary>
    ///     Everything a host client would normally do to turn a lobby into a running game. None of
    ///     it happens on its own in a server hosted lobby, because there is no host client to do it.
    /// </summary>
    internal partial class Game
    {
        private static readonly Dictionary<MapTypes, Type> ShipStatusTypes = new()
        {
            [MapTypes.Skeld] = typeof(InnerSkeldShipStatus),
            [MapTypes.MiraHQ] = typeof(InnerMiraShipStatus),
            [MapTypes.Polus] = typeof(InnerPolusShipStatus),
            [MapTypes.Dleks] = typeof(InnerDleksShipStatus),
            [MapTypes.Airship] = typeof(InnerAirshipStatus),
            [MapTypes.Fungle] = typeof(InnerFungleShipStatus),
        };

        /// <summary>
        ///     Starts the game on the server's own initiative.
        /// </summary>
        /// <returns>A <see cref="ValueTask" /> representing the asynchronous operation.</returns>
        /// <remarks>
        ///     <para>
        ///         The order here is what a client waits for, not what is tidy. A client sits on a
        ///         loading screen until the lobby has gone and the ship has arrived, gives up after
        ///         fifteen seconds, and will not leave that screen until every client is marked
        ///         ready and it has both a role and at least one task.
        ///     </para>
        /// </remarks>
        public async ValueTask StartAsync()
        {
            if (!IsServerHosted || GameState != GameStates.NotStarted)
            {
                return;
            }

            // Before anything else, while the lobby is still a lobby: removing a player only
            // tidies away their objects in the not started state.
            await RemoveFakePlayersAsync();

            GameState = GameStates.Starting;

            using (var packet = MessageWriter.Get(MessageType.Reliable))
            {
                // Clients do not read the body of this one; receiving it is the whole signal.
                packet.StartMessage(MessageFlags.StartGame);
                packet.Write(Code.Value);
                packet.EndMessage();

                await SendToAllAsync(packet);
            }

            await _eventManager.CallAsync(new GameStartingEvent(this));

            await DespawnLobbyBehaviourAsync();

            if (!await EnsureShipStatusAsync())
            {
                _logger.LogError("{Code} - Could not build the map, so the game cannot start.", Code);
                return;
            }

            await AssignRolesAsync();
            await AssignTasksAsync();

            // Characters the server owns will never announce themselves ready, and every client
            // waits for all of them before it will play.
            foreach (var player in _players.Values.Where(player => player.Client.Connection == null))
            {
                await SendReadyAsync(player.Client.Id);
            }

            await StartedAsync();
        }

        private static List<TaskData> Shuffle(IEnumerable<TaskData> tasks)
        {
            return tasks.OrderBy(_ => Random.Shared.Next()).ToList();
        }

        /// <summary>
        ///     Takes tasks off the front of a shuffled pool, preferring types the player does not
        ///     already have so they are not sent to the same console twice.
        /// </summary>
        private static void TakeTasks(ICollection<byte> assigned, ISet<TaskTypes> usedTypes, List<TaskData> pool, int count)
        {
            for (var taken = 0; taken < count && pool.Count > 0; taken++)
            {
                var index = pool.FindIndex(task => !usedTypes.Contains(task.Type));

                // Every remaining type is already in hand; a repeat beats being short of tasks.
                if (index < 0)
                {
                    index = 0;
                }

                var task = pool[index];
                pool.RemoveAt(index);
                usedTypes.Add(task.Type);
                assigned.Add((byte)task.Id);
            }
        }

        /// <summary>
        ///     Clears out the characters the server put in the lobby for show. They have no client
        ///     behind them, so left in place they would be dealt roles and stand in the map as
        ///     crewmates who never move and can never be voted out.
        /// </summary>
        /// <remarks>
        ///     The host seat is deliberately left alone. It has no character of its own and the
        ///     game still belongs to it.
        /// </remarks>
        private async ValueTask RemoveFakePlayersAsync()
        {
            var fakes = _players.Values
                .Where(player => player.Client.Connection == null && player.Character != null)
                .ToList();

            foreach (var fake in fakes)
            {
                await DespawnCharacterAsync(fake);

                // Tells the clients to forget them, and takes the PlayerInfo with it.
                await HandleRemovePlayer(fake.Client.Id, DisconnectReason.ExitGame);

                _logger.LogTrace("{Code} - Removed fake player {Name} for the game.", Code, fake.Client.Name);
            }
        }

        /// <summary>
        ///     Takes a player's character out of the world. Removing a player only despawns their
        ///     PlayerInfo, which would leave the body behind.
        /// </summary>
        private async ValueTask DespawnCharacterAsync(ClientPlayer player)
        {
            if (player.Character is not { } character)
            {
                return;
            }

            // Physics and the transform are registered alongside the control, each with its own
            // NetId, and each needs its own despawn message - not just the control's. A client
            // never told about the other two does not clean them up on its own; live testing
            // caught this as clients later trying (and failing, the server has already forgotten
            // the NetId) to despawn them themselves, minutes after the fact.
            foreach (var component in character.GetComponentsInChildren<InnerNetObject>())
            {
                await SendObjectDespawnAsync(component);
                RemoveNetObject(component);
            }

            player.Character = null;
        }

        /// <summary>
        ///     Takes the lobby away. Clients treat the lobby still being present as the map not
        ///     having loaded yet, and eventually disconnect over it.
        /// </summary>
        private async ValueTask DespawnLobbyBehaviourAsync()
        {
            if (GameNet.LobbyBehaviour is not { } lobby)
            {
                return;
            }

            await SendObjectDespawnAsync(lobby);

            RemoveNetObject(lobby);
            GameNet.LobbyBehaviour = null;

            _logger.LogTrace("{Code} - Despawned LobbyBehaviour (netId {NetId})", Code, lobby.NetId);
        }

        /// <summary>
        ///     Takes the map away: every door, every completed task, every dead body, gone.
        /// </summary>
        /// <remarks>
        ///     A real host's own client manages this on its own initiative as part of ending a
        ///     round - despawning the map is just one more message it happens to send, which
        ///     Impostor relays like anything else a host sends, never having to think about the
        ///     map's lifecycle itself. A server-hosted game has no host client to do that, so left
        ///     alone the map just sits there fully spawned in behind whatever comes next - which is
        ///     exactly what a rejoin after a round ended showed: the lobby's own UI drawn over a
        ///     map that had never actually gone anywhere. Only ever meant for that case; see the
        ///     call site.
        /// </remarks>
        private async ValueTask DespawnShipStatusAsync()
        {
            if (GameNet.ShipStatus is not { } ship)
            {
                return;
            }

            await SendObjectDespawnAsync(ship);

            RemoveNetObject(ship);
            GameNet.ShipStatus = null;

            _logger.LogTrace("{Code} - Despawned {Map} (netId {NetId})", Code, Options.Map, ship.NetId);
        }

        /// <summary>
        ///     Builds the map for the lobby's chosen map type.
        /// </summary>
        /// <returns>True if the map is now present.</returns>
        private async ValueTask<bool> EnsureShipStatusAsync()
        {
            if (GameNet.ShipStatus != null)
            {
                return true;
            }

            if (!ShipStatusTypes.TryGetValue(Options.Map, out var shipStatusType))
            {
                _logger.LogError("{Code} - No ship status is known for map {Map}.", Code, Options.Map);
                return false;
            }

            var ship = (InnerShipStatus)ActivatorUtilities.CreateInstance(_serviceProvider, shipStatusType, this);
            ship.SpawnFlags = SpawnFlags.None;

            if (!RegisterServerObject(ship, ServerOwned))
            {
                return false;
            }

            GameNet.ShipStatus = ship;

            // Builds the doors and the sabotage systems. Nothing spawned server side goes through
            // the incoming spawn path that normally does this, and the spawn payload below is
            // written from those systems.
            await ship.OnSpawnAsync();

            _logger.LogTrace("{Code} - Spawning {Map} (netId {NetId})", Code, Options.Map, ship.NetId);
            await SendObjectSpawnAsync(ship);

            return true;
        }

        /// <summary>
        ///     Marks a client as loaded and ready to play, on its behalf.
        /// </summary>
        private async ValueTask SendReadyAsync(int clientId)
        {
            using var writer = StartGameData();
            writer.StartMessage(GameDataTag.ReadyFlag);
            writer.WritePacked(clientId);
            writer.EndMessage();
            await FinishGameDataAsync(writer);
        }

        /// <summary>
        ///     Deals out roles. Vanilla broadcasts every role to everyone and leaves it to the
        ///     client to decide what to show, so this does the same.
        /// </summary>
        private async ValueTask AssignRolesAsync()
        {
            var players = _players.Values
                .Where(player => player.Character?.PlayerInfo != null)
                .OrderBy(player => player.Client.Id)
                .ToList();

            // Only real players are eligible, and never all of them, so a game can never begin
            // already won. With a single player that leaves nobody, which is what makes a
            // one player game sit there rather than ending the moment it starts.
            var candidates = players
                .Where(player => player.Client.Connection != null)
                .ToList();

            var impostorCount = Math.Clamp(Options.NumImpostors, 0, Math.Max(0, candidates.Count - 1));

            var impostors = candidates
                .OrderBy(_ => Random.Shared.Next())
                .Take(impostorCount)
                .ToHashSet();

            foreach (var player in players)
            {
                var role = impostors.Contains(player) ? RoleTypes.Impostor : RoleTypes.Crewmate;
                await player.Character!.SetRoleAsync(role);
            }

            _logger.LogInformation(
                "{Code} - Started with {Impostors} impostor(s) out of {Players} player(s).",
                Code,
                impostorCount,
                candidates.Count);
        }

        /// <summary>
        ///     Deals out tasks. A client will not finish loading until it has at least one, even
        ///     when its role has no real use for them.
        /// </summary>
        private async ValueTask AssignTasksAsync()
        {
            if (GameNet.ShipStatus is not { } ship)
            {
                return;
            }

            var options = Options as NormalGameOptions;
            var commonCount = options?.NumCommonTasks ?? 1;
            var longCount = options?.NumLongTasks ?? 1;
            var shortCount = options?.NumShortTasks ?? 2;

            var tasks = ship.Data.Tasks.Values;
            var common = tasks.Where(task => task.Category == TaskCategories.CommonTask).ToList();
            var longTasks = tasks.Where(task => task.Category == TaskCategories.LongTask).ToList();
            var shortTasks = tasks.Where(task => task.Category == TaskCategories.ShortTask).ToList();

            // Everybody gets the same common tasks, which is the point of them.
            var shared = new List<byte>();
            TakeTasks(shared, new HashSet<TaskTypes>(), Shuffle(common), commonCount);

            foreach (var player in _players.Values)
            {
                if (player.Character?.PlayerInfo is not { } playerInfo)
                {
                    continue;
                }

                var assigned = new List<byte>(shared);
                var usedTypes = shared.Count == 0
                    ? new HashSet<TaskTypes>()
                    : common.Where(task => shared.Contains((byte)task.Id)).Select(task => task.Type).ToHashSet();

                TakeTasks(assigned, usedTypes, Shuffle(longTasks), longCount);
                TakeTasks(assigned, usedTypes, Shuffle(shortTasks), shortCount);

                playerInfo.SetTasks(assigned.ToArray());

                using var writer = StartRpc(playerInfo.NetId, RpcCalls.SetTasks);
                Rpc29SetTasks.Serialize(writer, assigned.ToArray());
                await FinishRpcAsync(writer);
            }
        }
    }
}
