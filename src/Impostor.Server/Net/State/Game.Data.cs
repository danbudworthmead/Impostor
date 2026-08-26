using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Impostor.Api;
using Impostor.Api.Innersloth;
using Impostor.Api.Net.Inner;
using Impostor.Api.Unity;
using Impostor.Server.Events.Meeting;
using Impostor.Server.Events.Player;
using Impostor.Server.Net.Inner;
using Impostor.Server.Net.Inner.Objects;
using Impostor.Server.Net.Inner.Objects.Components;
using Impostor.Server.Net.Inner.Objects.GameManager;
using Impostor.Server.Net.Inner.Objects.ShipStatus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Impostor.Server.Net.State
{
    internal partial class Game
    {
        /// <summary>
        ///     Used for global object, spawned by the host.
        /// </summary>
        private const int InvalidClient = -2;

        /// <summary>
        ///     Used internally to set the OwnerId to the current ClientId.
        ///     i.e: <code>ownerId = ownerId == -3 ? this.ClientId : ownerId;</code>
        /// </summary>
        private const int CurrentClient = -3;

        /// <summary>
        ///     Used to list objects that are managed by the game server.
        /// </summary>
        private const int ServerOwned = -4;

        /// <summary>
        ///     The first NetId that is considered as a server owned Network ID that the client will not allocate by default.
        /// </summary>
        private const int MinServerNetId = 100000;

        private static readonly Dictionary<uint, Type> SpawnableObjects = new()
        {
            [0] = typeof(InnerSkeldShipStatus),
            [1] = typeof(InnerMeetingHud),
            [2] = typeof(InnerLobbyBehaviour),
            [4] = typeof(InnerPlayerControl),
            [5] = typeof(InnerMiraShipStatus),
            [6] = typeof(InnerPolusShipStatus),
            [7] = typeof(InnerDleksShipStatus),
            [8] = typeof(InnerAirshipStatus),
            [9] = typeof(InnerHideAndSeekManager),
            [10] = typeof(InnerNormalGameManager),
            [11] = typeof(InnerPlayerInfo),
            [12] = typeof(InnerVoteBanSystem),
            [13] = typeof(InnerFungleShipStatus),
        };

        private static readonly Dictionary<Type, uint> SpawnableObjectIds = SpawnableObjects.ToDictionary((i) => i.Value, (i) => i.Key);

        private readonly ConcurrentDictionary<uint, InnerNetObject> _allObjects = new ConcurrentDictionary<uint, InnerNetObject>();

        private uint _nextNetId = MinServerNetId;

        public T? FindObjectByNetId<T>(uint netId)
            where T : IInnerNetObject
        {
            if (_allObjects.TryGetValue(netId, out var obj))
            {
                return (T)(IInnerNetObject)obj;
            }

            return default;
        }

        public async ValueTask<bool> HandleGameDataAsync(IMessageReader parent, ClientPlayer sender, bool toPlayer)
        {
            // Find target player.
            ClientPlayer? target = null;

            if (toPlayer)
            {
                var targetId = parent.ReadPackedInt32();
                if (!TryGetPlayer(targetId, out target))
                {
                    _logger.LogWarning("Player {0} tried to send GameData to unknown player {1}.", sender.Client.Id, targetId);
                    return false;
                }

                _logger.LogTrace("Received GameData for target {0}.", targetId);
            }

            // Parse GameData messages.
            while (parent.Position < parent.Length)
            {
                using var reader = parent.ReadMessage();

                switch (reader.Tag)
                {
                    case GameDataTag.DataFlag:
                    {
                        var netId = reader.ReadPackedUInt32();
                        if (_allObjects.TryGetValue(netId, out var obj))
                        {
                            await obj.DeserializeAsync(sender, target, reader, false);
                        }
                        else
                        {
                            _logger.LogWarning("Received DataFlag for unregistered NetId {0}.", netId);
                        }

                        break;
                    }

                    case GameDataTag.RpcFlag:
                    {
                        var netId = reader.ReadPackedUInt32();
                        if (_allObjects.TryGetValue(netId, out var obj))
                        {
                            if (!await obj.HandleRpcAsync(sender, target, (RpcCalls)reader.ReadByte(), reader))
                            {
                                parent.RemoveMessage(reader);
                                continue;
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Received RpcFlag for unregistered NetId {0}.", netId);
                        }

                        break;
                    }

                    case GameDataTag.SpawnFlag:
                    {
                        // Only the host is allowed to spawn objects.
                        if (!sender.IsHost)
                        {
                            if (await sender.Client.ReportCheatAsync(new CheatContext(nameof(GameDataTag.SpawnFlag)), CheatCategory.MustBeHost, "Tried to send SpawnFlag as non-host."))
                            {
                                return false;
                            }
                        }

                        var objectId = reader.ReadPackedUInt32();
                        if (SpawnableObjects.TryGetValue(objectId, out var spawnableObjectType))
                        {
                            var innerNetObject = (InnerNetObject)ActivatorUtilities.CreateInstance(_serviceProvider, spawnableObjectType, this);
                            var ownerClientId = reader.ReadPackedInt32();

                            innerNetObject.SpawnFlags = (SpawnFlags)reader.ReadByte();

                            var components = innerNetObject.GetComponentsInChildren<InnerNetObject>();
                            var componentsCount = reader.ReadPackedInt32();

                            if (componentsCount != components.Count)
                            {
                                _logger.LogError(
                                    "Children didn't match for spawnable {0}, name {1} ({2} != {3})",
                                    objectId,
                                    innerNetObject.GetType().Name,
                                    componentsCount,
                                    components.Count);
                                continue;
                            }

                            _logger.LogDebug(
                                "Spawning {0} components, SpawnFlags {1}",
                                innerNetObject.GetType().Name,
                                innerNetObject.SpawnFlags);

                            for (var i = 0; i < componentsCount; i++)
                            {
                                var obj = components[i];

                                obj.NetId = reader.ReadPackedUInt32();
                                obj.OwnerId = ownerClientId;

                                _logger.LogDebug(
                                    "- {0}, NetId {1}, OwnerId {2}",
                                    obj.GetType().Name,
                                    obj.NetId,
                                    obj.OwnerId);

                                if (!AddNetObject(obj))
                                {
                                    _logger.LogTrace("Failed to AddNetObject, it already exists.");

                                    obj.NetId = uint.MaxValue;
                                    break;
                                }

                                using var readerSub = reader.ReadMessage();
                                if (readerSub.Length > 0)
                                {
                                    await obj.DeserializeAsync(sender, target, readerSub, true);
                                }

                                await OnSpawnAsync(sender, obj);
                            }

                            continue;
                        }

                        _logger.LogWarning("Couldn't find spawnable object {0}.", objectId);
                        break;
                    }

                    // Only the host is allowed to despawn objects.
                    case GameDataTag.DespawnFlag:
                    {
                        var netId = reader.ReadPackedUInt32();
                        if (_allObjects.TryGetValue(netId, out var obj))
                        {
                            if (sender.Client.Id != obj.OwnerId && !sender.IsHost)
                            {
                                _logger.LogWarning(
                                    "Player {0} ({1}) tried to send DespawnFlag for {2} but was denied.",
                                    sender.Client.Name,
                                    sender.Client.Id,
                                    netId);
                                return false;
                            }

                            RemoveNetObject(obj);
                            await OnDestroyAsync(obj);
                            _logger.LogDebug("Destroyed InnerNetObject {0} ({1}), OwnerId {2}", obj.GetType().Name, netId, obj.OwnerId);
                        }
                        else
                        {
                            _logger.LogDebug(
                                "Player {0} ({1}) sent DespawnFlag for unregistered NetId {2}.",
                                sender.Client.Name,
                                sender.Client.Id,
                                netId);
                        }

                        break;
                    }

                    case GameDataTag.SceneChangeFlag:
                    {
                        // Sender is only allowed to change his own scene.
                        var clientId = reader.ReadPackedInt32();
                        var scene = reader.ReadString();

                        if (clientId != sender.Client.Id)
                        {
                            _logger.LogWarning(
                                "Player {0} ({1}) tried to send SceneChangeFlag for another player.",
                                sender.Client.Name,
                                sender.Client.Id);
                            return false;
                        }

                        // According to game assembly, sender is only allowed to send OnlineGame.
                        if (scene != "OnlineGame")
                        {
                            _logger.LogWarning(
                                "Player {PlayerName} ({ClientId}) tried to send SceneChangeFlag with disallowed scene \"{Scene}\".",
                                sender.Client.Name,
                                sender.Client.Id,
                                scene);
                            return false;
                        }

                        sender.Scene = scene;

                        _logger.LogTrace("> Scene {0} to {1}", clientId, sender.Scene);

                        // When the server is host it has to build the scene the player arrives
                        // into, and then give them a character to control.
                        if (IsServerHosted)
                        {
                            await EnsureLobbyBehaviourAsync();
                        }

                        await SyncServerObjectsAsync(sender);
                        await SpawnPlayerInfoAsync(sender);

                        if (IsServerHosted)
                        {
                            await SpawnPlayerControlAsync(sender);
                        }

                        break;
                    }

                    case GameDataTag.ReadyFlag:
                    {
                        var clientId = reader.ReadPackedInt32();

                        if (clientId != sender.Client.Id)
                        {
                            _logger.LogWarning(
                                "Player {0} ({1}) tried to send ReadyFlag for another player.",
                                sender.Client.Name,
                                sender.Client.Id);
                            return false;
                        }

                        _logger.LogTrace("> IsReady {0}", clientId);
                        break;
                    }

                    case GameDataTag.ConsoleDeclareClientPlatformFlag:
                    {
                        var clientId = reader.ReadPackedInt32();
                        var platform = (RuntimePlatform)reader.ReadPackedInt32();

                        if (clientId != sender.Client.Id)
                        {
                            if (await sender.Client.ReportCheatAsync(new CheatContext(nameof(GameDataTag.ConsoleDeclareClientPlatformFlag)), CheatCategory.Ownership, "Client sent info with wrong client id"))
                            {
                                return false;
                            }
                        }

                        sender.Platform = platform;

                        break;
                    }

                    default:
                    {
                        _logger.LogWarning("Bad GameData tag {0}", reader.Tag);
                        break;
                    }
                }

                if (sender.Client.Player == null)
                {
                    // Disconnect handler was probably invoked, cancel the rest.
                    return false;
                }
            }

            return true;
        }

        private async ValueTask OnSpawnAsync(ClientPlayer sender, InnerNetObject netObj)
        {
            switch (netObj)
            {
                case InnerGameManager innerGameManager:
                {
                    GameNet.GameManager = innerGameManager;
                    break;
                }

                case InnerLobbyBehaviour lobby:
                {
                    GameNet.LobbyBehaviour = lobby;
                    break;
                }

                case InnerPlayerInfo playerInfo:
                {
                    if (!GameNet.GameData.AddPlayer(playerInfo))
                    {
                        _logger.LogWarning(
                            "Could not add PlayerInfo for playerId {PlayerId} with NetId {newId}, already have NetId {oldNetId}",
                            playerInfo.PlayerId,
                            playerInfo.NetId,
                            GameNet.GameData.GetPlayerById(playerInfo.PlayerId)?.NetId);
                    }

                    break;
                }

                case InnerVoteBanSystem voteBan:
                {
                    GameNet.VoteBan = voteBan;
                    break;
                }

                case InnerShipStatus shipStatus:
                {
                    GameNet.ShipStatus = shipStatus;
                    break;
                }

                case InnerPlayerControl control:
                {
                    // Hook up InnerPlayerControl <-> IClientPlayer.
                    if (TryGetPlayer(control.OwnerId, out var player))
                    {
                        player.Character = control;
                        player.DisableSpawnTimeout();
                    }
                    else
                    {
                        await sender.Client.ReportCheatAsync(new CheatContext(nameof(GameDataTag.SpawnFlag)), CheatCategory.GameFlow, "Failed to find player that spawned the InnerPlayerControl");
                    }

                    // Hook up InnerPlayerControl <-> InnerPlayerControl.PlayerInfo.
                    var playerInfo = GameNet.GameData.GetPlayerById(control.PlayerId);

                    if (playerInfo != null)
                    {
                        playerInfo.Controller = control;
                        control.PlayerInfo = playerInfo;
                    }

                    if (player != null)
                    {
                        await _eventManager.CallAsync(new PlayerSpawnedEvent(this, player, control));
                    }

                    break;
                }

                case InnerMeetingHud meetingHud:
                {
                    foreach (var player in _players.Values)
                    {
                        if (GameNet.ShipStatus != null)
                        {
                            await player.Character!.NetworkTransform.SetPositionAsync(player, GameNet.ShipStatus.GetSpawnLocation(player.Character, PlayerCount, false));
                        }
                    }

                    await _eventManager.CallAsync(new MeetingStartedEvent(this, meetingHud));
                    break;
                }
            }

            await netObj.OnSpawnAsync();
        }

        private async ValueTask OnDestroyAsync(InnerNetObject netObj)
        {
            switch (netObj)
            {
                case InnerLobbyBehaviour:
                {
                    GameNet.LobbyBehaviour = null;
                    break;
                }

                case InnerVoteBanSystem:
                {
                    GameNet.VoteBan = null;
                    break;
                }

                case InnerShipStatus:
                {
                    GameNet.ShipStatus = null;
                    break;
                }

                case InnerPlayerInfo playerInfo:
                {
                    if (GameState != GameStates.Started && GameState != GameStates.Starting)
                    {
                        GameNet.GameData.RemovePlayer(playerInfo.PlayerId);
                    }

                    break;
                }

                case InnerPlayerControl control:
                {
                    // Remove InnerPlayerControl <-> IClientPlayer.
                    if (TryGetPlayer(control.OwnerId, out var player))
                    {
                        player.Character = null;
                        await _eventManager.CallAsync(new PlayerDestroyedEvent(this, player, control));
                    }

                    break;
                }
            }
        }

        private async ValueTask SyncServerObjectsAsync(ClientPlayer sender)
        {
            foreach (var obj in _allObjects.Values)
            {
                // Components are registered individually so their RPCs resolve, but they travel
                // inside their parent's spawn message. Only spawn roots are sent.
                if (!SpawnableObjectIds.ContainsKey(obj.GetType()))
                {
                    continue;
                }

                // Normally the host client tells a newcomer about everyone else's objects. When
                // the server is host nobody else will, so send those too. The player's own
                // character is spawned separately, right after this.
                var isOwnedByAnotherPlayer = IsServerHosted && obj.OwnerId != sender.Client.Id;

                if (obj.OwnerId == ServerOwned || isOwnedByAnotherPlayer)
                {
                    _logger.LogTrace("Syncing {Type} {NetId}", obj.GetType(), obj.NetId);
                    await SendObjectSpawnAsync(obj, sender.Client.Id);
                }
            }
        }

        /// <summary>
        ///     Gives a server created object and each of its components their own network id and
        ///     registers them, mirroring how ids arrive when a host client spawns something.
        /// </summary>
        private bool RegisterServerObject(InnerNetObject obj, int ownerId)
        {
            foreach (var component in obj.GetComponentsInChildren<InnerNetObject>())
            {
                component.NetId = _nextNetId++;
                component.OwnerId = ownerId;

                if (!AddNetObject(component))
                {
                    _logger.LogError("Couldn't register {Type} while spawning it server side", component.GetType().Name);
                    component.NetId = uint.MaxValue;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        ///     Creates the lobby players stand in. A host client spawns this when it reaches the
        ///     lobby scene, so when the server is host nobody would otherwise create it.
        /// </summary>
        private async ValueTask EnsureLobbyBehaviourAsync()
        {
            if (GameNet.LobbyBehaviour != null || GameState != GameStates.NotStarted)
            {
                return;
            }

            var lobby = (InnerLobbyBehaviour)ActivatorUtilities.CreateInstance(_serviceProvider, typeof(InnerLobbyBehaviour), this);
            lobby.SpawnFlags = SpawnFlags.None;

            if (!RegisterServerObject(lobby, ServerOwned))
            {
                return;
            }

            GameNet.LobbyBehaviour = lobby;

            _logger.LogTrace("Spawning LobbyBehaviour (netId {NetId})", lobby.NetId);
            await SendObjectSpawnAsync(lobby);
        }

        /// <summary>
        ///     Creates a player's character, which a host client would normally spawn on their
        ///     behalf. It is owned by that player so they keep control of their own movement.
        /// </summary>
        private async ValueTask SpawnPlayerControlAsync(ClientPlayer sender)
        {
            if (sender.Character != null)
            {
                return;
            }

            // The character is matched to its PlayerInfo by player id, so that must exist first.
            if (!GameNet.GameData.PlayersByClientId.TryGetValue(sender.Client.Id, out var playerInfo))
            {
                _logger.LogWarning("No PlayerInfo to attach a character to for client {ClientId}", sender.Client.Id);
                return;
            }

            var control = (InnerPlayerControl)ActivatorUtilities.CreateInstance(_serviceProvider, typeof(InnerPlayerControl), this);
            control.SpawnFlags = SpawnFlags.IsClientCharacter;
            control.IsNew = true;
            control.PlayerId = playerInfo.PlayerId;

            if (!RegisterServerObject(control, sender.Client.Id))
            {
                return;
            }

            _logger.LogTrace("Spawning PlayerControl for client {ClientId} (netId {NetId})", sender.Client.Id, control.NetId);
            await OnSpawnAsync(sender, control);
            await SendObjectSpawnAsync(control);
        }

        private async ValueTask SpawnPlayerInfoAsync(ClientPlayer sender)
        {
            // Hosts spawn PlayerInfo objects if they requested authority
            if (IsHostAuthoritive)
            {
                return;
            }

            // Only spawn a new PlayerInfo if one has not yet been spawned
            if (GameNet.GameData.PlayersByClientId.ContainsKey(sender.Client.Id))
            {
                return;
            }

            var playerInfo = (InnerPlayerInfo)ActivatorUtilities.CreateInstance(_serviceProvider, typeof(InnerPlayerInfo), this);
            playerInfo.SpawnFlags = SpawnFlags.None;
            playerInfo.NetId = _nextNetId++;
            playerInfo.OwnerId = ServerOwned;
            playerInfo.ClientId = sender.Client.Id;

            // This object is server-owned, so what is written here is the only source other
            // clients ever see for these fields.
            playerInfo.FriendCode = sender.Client.FriendCode;
            playerInfo.ProductUserId = sender.Client.ProductUserId;
            playerInfo.PlayerId = GameNet.GameData.GetNextAvailablePlayerId();

            // If player played a previous game, restore their color
            var prevColor = sender.Client.PreviousColor;
            if (prevColor.HasValue)
            {
                _logger.LogTrace("Color restored to {Color}", prevColor.Value);
                playerInfo.CurrentOutfit.Color = prevColor.Value;
            }

            if (!AddNetObject(playerInfo))
            {
                _logger.LogError("Couldn't spawn PlayerInfo for {Name} ({ClientId})", sender.Client.Name, sender.Client.Id);
                playerInfo.NetId = uint.MaxValue;
                return;
            }

            _logger.LogTrace("Spawning PlayerInfo (netId {Netid})", playerInfo.NetId);
            await OnSpawnAsync(sender, playerInfo);
            await SendObjectSpawnAsync(playerInfo);
        }

        private async ValueTask DespawnPlayerInfoAsync(InnerPlayerInfo playerInfo)
        {
            if (playerInfo.OwnerId == ServerOwned)
            {
                _logger.LogDebug("Despawning PlayerInfo {nid}", playerInfo.NetId);
                GameNet.GameData.RemovePlayer(playerInfo.PlayerId);
                RemoveNetObject(playerInfo);

                await SendObjectDespawnAsync(playerInfo);
            }
        }

        private bool AddNetObject(InnerNetObject obj)
        {
            return _allObjects.TryAdd(obj.NetId, obj);
        }

        private void RemoveNetObject(InnerNetObject obj)
        {
            _allObjects.TryRemove(obj.NetId, out _);
        }
    }
}
