using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Impostor.Api.Games;
using Impostor.Api.Innersloth;
using Impostor.Api.Innersloth.GameOptions;
using Impostor.Api.Net;
using Impostor.Api.Net.Inner;
using Impostor.Api.Net.Messages;
using Impostor.Api.Net.Messages.C2S;
using Impostor.Api.Net.Messages.S2C;
using Impostor.Api.Net.Messages.Rpcs;
using Impostor.Hazel;
using Impostor.Hazel.Abstractions;
using Impostor.Hazel.Udp;

namespace Impostor.Client
{
    /// <summary>
    /// A minimal, real network participant: it speaks the actual wire protocol (informed by the
    /// decompiled client) rather than anything server-side pretending to be a player, so tests
    /// built on it exercise the same code paths a real player's game does. Deliberately narrow -
    /// enough to join, learn who else is in the lobby, and act as either side of a murder. Not a
    /// full client: no movement, no tasks, no rendering, nothing UI-shaped.
    /// </summary>
    public sealed class BotClient : IAsyncDisposable
    {
        /// <summary>
        /// Newest version CompatibilityManager.DefaultSupportedVersions currently accepts.
        /// </summary>
        public static readonly GameVersion ProtocolVersion = new(2026, 3, 18);

        private readonly UdpClientConnection _connection;
        private readonly string _name;
        private readonly TaskCompletionSource<GameCode> _hostedGameCode = new();
        private readonly Dictionary<int, uint> _characterNetIdByClientId = new();
        private readonly Dictionary<uint, int> _ownerByCharacterNetId = new();

        public BotClient(IPEndPoint endpoint, string name)
        {
            _name = name;
            _connection = new UdpClientConnection(endpoint, null)
            {
                DataReceived = OnDataReceived,
                Disconnected = OnDisconnected,
            };
        }

        /// <summary>
        /// This client's own client id, once the server has assigned one. Unset before joining.
        /// </summary>
        public int ClientId { get; private set; } = -1;

        /// <summary>
        /// This client's own character's net id, once it has spawned. Unset before then.
        /// </summary>
        public uint? CharacterNetId { get; private set; }

        public GameCode GameCode { get; private set; }

        public int HostId { get; private set; } = -1;

        /// <summary>
        /// Role this client's own character was last told it has. Set from the SetRole RPC, the
        /// same one a real client's kill button visibility reacts to.
        /// </summary>
        public RoleTypes? Role { get; private set; }

        /// <summary>
        /// Set once this client's own character has been targeted by a MurderPlayer RPC.
        /// </summary>
        public bool WasMurdered { get; private set; }

        public string? DisconnectReason { get; private set; }

        public async ValueTask ConnectAsync()
        {
            using var handshake = MessageWriter.Get(MessageType.Reliable);

            handshake.Write(ProtocolVersion);
            handshake.Write(_name);
            handshake.Write(0u); // lastNonceReceived, always 0
            handshake.Write((uint)Language.English);
            handshake.Write((byte)QuickChatModes.FreeChatOrQuickChat);

            var platformData = new PlatformSpecificData(Platforms.Unknown, _name);
            platformData.Serialize(handshake);

            handshake.Write(0); // crossplayFlags, not used
            handshake.Write((byte)0); // purpose unknown, hardcoded to 0 upstream too

            await _connection.ConnectAsync(handshake.ToByteArray(false));
        }

        /// <summary>
        /// Hosts a new game and returns its code. Only works when the server has
        /// AllowServerAsHost on - the same config flag production runs under - since this client
        /// hands the seat straight back to the server, the same as a real first player would.
        /// </summary>
        public async ValueTask<GameCode> HostGameAsync()
        {
            using var writer = MessageWriter.Get(MessageType.Reliable);

            Message00HostGameC2S.Serialize(
                writer,
                new NormalGameOptions(),
                CrossplayFlags.All,
                GameFilterOptions.CreateDefault());

            await _connection.SendAsync(writer);

            GameCode = await _hostedGameCode.Task;
            return GameCode;
        }

        public async ValueTask JoinGameAsync(GameCode code)
        {
            GameCode = code;

            using var writer = MessageWriter.Get(MessageType.Reliable);
            Message01JoinGameC2S.Serialize(writer, code);
            await _connection.SendAsync(writer);
        }

        /// <summary>
        /// Sends CheckMurder for the given target, exactly as a real client's kill button does -
        /// server side arbitration decides whether it succeeds.
        /// </summary>
        public async ValueTask SendCheckMurderAsync(uint targetNetId)
        {
            if (CharacterNetId is not { } ownNetId)
            {
                throw new InvalidOperationException("Cannot murder before this client's own character has spawned.");
            }

            using var writer = MessageWriter.Get(MessageType.Reliable);

            writer.StartMessage(MessageFlags.GameDataTo);
            GameCode.Serialize(writer);
            writer.WritePacked(HostId);

            writer.StartMessage(GameDataTags.RpcFlag);
            writer.WritePacked(ownNetId);
            writer.Write((byte)RpcCalls.CheckMurder);
            writer.WritePacked(targetNetId);
            writer.EndMessage();

            writer.EndMessage();

            await _connection.SendAsync(writer);
        }

        /// <summary>
        /// The character net id belonging to a given client, once seen spawn. Null if that client
        /// has not spawned a character yet, as far as this bot has observed.
        /// </summary>
        public uint? CharacterNetIdOf(int clientId)
        {
            return _characterNetIdByClientId.TryGetValue(clientId, out var netId) ? netId : null;
        }

        private ValueTask OnDisconnected(DisconnectedEventArgs e)
        {
            DisconnectReason = e.Reason;
            return default;
        }

        private async ValueTask OnDataReceived(DataReceivedEventArgs e)
        {
            Console.WriteLine($"[BotClient {_name}] OnDataReceived, length={e.Message.Length}, position={e.Message.Position}, tag={e.Message.Tag}, type={e.Type}");
            HandleMessage(e.Message);
            await ValueTask.CompletedTask;
        }

        private void HandleMessage(IMessageReader message)
        {
            switch (message.Tag)
            {
                case MessageFlags.HostGame:
                {
                    Message00HostGameS2C.Deserialize(message, out var code);
                    GameCode = code;
                    _hostedGameCode.TrySetResult(code);
                    break;
                }

                case MessageFlags.JoinedGame:
                {
                    HandleJoinedGame(message);
                    break;
                }

                case MessageFlags.GameData:
                case MessageFlags.GameDataTo:
                {
                    HandleGameData(message);
                    break;
                }
            }
        }

        private void HandleJoinedGame(IMessageReader message)
        {
            GameCode = message.ReadInt32();
            ClientId = message.ReadInt32();
            HostId = message.ReadInt32();

            var otherCount = message.ReadPackedInt32();

            for (var i = 0; i < otherCount; i++)
            {
                var clientId = message.ReadPackedInt32();
                message.ReadString(); // name
                using (var platform = message.ReadMessage())
                {
                    // Not tracked; only present so the stream stays aligned.
                }

                message.ReadPackedUInt32(); // player level
                message.ReadString(); // product user id
                message.ReadString(); // friend code

                // Not yet spawned as far as this message is concerned - its character arrives
                // later as an ordinary SpawnFlag, same as anyone else's.
                _ = clientId;
            }
        }

        private void HandleGameData(IMessageReader message)
        {
            // GameDataTo carries an extra target client id ahead of the payload.
            if (message.Tag == MessageFlags.GameDataTo)
            {
                message.ReadPackedInt32();
            }

            while (message.Position < message.Length)
            {
                using var sub = message.ReadMessage();

                switch (sub.Tag)
                {
                    case GameDataTags.SpawnFlag:
                        HandleSpawn(sub);
                        break;

                    case GameDataTags.RpcFlag:
                        HandleRpc(sub);
                        break;
                }
            }
        }

        private void HandleSpawn(IMessageReader message)
        {
            var spawnId = message.ReadPackedUInt32();
            var ownerId = message.ReadPackedInt32();
            message.ReadByte(); // spawn flags

            var componentCount = message.ReadPackedInt32();
            uint? firstNetId = null;

            for (var i = 0; i < componentCount; i++)
            {
                var netId = message.ReadPackedUInt32();
                firstNetId ??= netId;

                using var data = message.ReadMessage();

                // Contents deliberately unparsed - this bot only needs to know the netId exists
                // and who owns it, not the state inside it.
            }

            // spawnId 4 is InnerPlayerControl (SpawnableObjects in Game.Data.cs); its own net id
            // is always the first component, matching component registration order server side.
            if (spawnId != 4 || firstNetId is not { } characterNetId)
            {
                return;
            }

            _characterNetIdByClientId[ownerId] = characterNetId;
            _ownerByCharacterNetId[characterNetId] = ownerId;

            if (ownerId == ClientId)
            {
                CharacterNetId = characterNetId;
            }
        }

        private void HandleRpc(IMessageReader message)
        {
            var targetNetId = message.ReadPackedUInt32();
            var call = (RpcCalls)message.ReadByte();

            switch (call)
            {
                case RpcCalls.SetRole:
                {
                    var role = (RoleTypes)message.ReadUInt16();
                    message.ReadBoolean(); // canOverrideRole

                    if (targetNetId == CharacterNetId)
                    {
                        Role = role;
                    }

                    break;
                }

                case RpcCalls.MurderPlayer:
                {
                    var victimNetId = message.ReadPackedUInt32();
                    message.ReadInt32(); // MurderResultFlags

                    if (victimNetId == CharacterNetId)
                    {
                        WasMurdered = true;
                    }

                    break;
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            _connection.Dispose();
            return default;
        }

        /// <summary>
        /// Local mirror of Impostor.Server.Net.Inner.GameDataTag, which is internal to the server
        /// and not part of the public API surface this project can reference.
        /// </summary>
        private static class GameDataTags
        {
            public const byte SpawnFlag = 4;
            public const byte RpcFlag = 2;
        }
    }
}
