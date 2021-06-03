using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Impostor.Api;
using Impostor.Api.Events.Managers;
using Impostor.Api.Innersloth;
using Impostor.Api.Innersloth.Maps;
using Impostor.Api.Net;
using Impostor.Api.Net.Custom;
using Impostor.Api.Net.Inner;
using Impostor.Api.Net.Inner.Objects;
using Impostor.Api.Net.Inner.Objects.ShipStatus;
using Impostor.Api.Net.Messages.Rpcs;
using Impostor.Hazel;
using Impostor.Server.Events.Player;
using Impostor.Server.Net.Inner.Objects.Systems;
using Impostor.Server.Net.Inner.Objects.Systems.ShipStatus;
using Impostor.Server.Net.State;

namespace Impostor.Server.Net.Inner.Objects.ShipStatus
{
    internal abstract class InnerShipStatus : InnerNetObject, IInnerShipStatus
    {
        private readonly Dictionary<SystemTypes, ISystemType> _systems = new Dictionary<SystemTypes, ISystemType>();
        private IEventManager _eventManager;

        protected InnerShipStatus(ICustomMessageManager<ICustomRpc> customMessageManager, Game game, IEventManager eventManager) : base(customMessageManager, game)
        {
            Components.Add(this);
            _eventManager = eventManager;
        }

        public abstract IMapData Data { get; }

        public abstract Dictionary<int, bool> Doors { get; }

        public abstract float SpawnRadius { get; }

        public abstract Vector2 InitialSpawnCenter { get; }

        public abstract Vector2 MeetingSpawnCenter { get; }

        internal override ValueTask OnSpawnAsync()
        {
            for (var i = 0; i < Doors.Count; i++)
            {
                Doors.Add(i, false);
            }

            AddSystems(_systems);
            _systems.Add(SystemTypes.Sabotage, new SabotageSystemType(_systems.Values.OfType<IActivatable>().ToArray()));

            return base.OnSpawnAsync();
        }

        public override ValueTask<bool> SerializeAsync(IMessageWriter writer, bool initialState)
        {
            throw new NotImplementedException();
        }

        public override async ValueTask DeserializeAsync(IClientPlayer sender, IClientPlayer? target, IMessageReader reader, bool initialState)
        {
            if (!await ValidateHost(CheatContext.Deserialize, sender) || !await ValidateBroadcast(CheatContext.Deserialize, sender, target))
            {
                return;
            }

            while (reader.Position < reader.Length)
            {
                var messageReader = reader.ReadMessage();
                var type = (SystemTypes)messageReader.Tag;
                if (_systems.TryGetValue(type, out var value))
                {
                    value.Deserialize(messageReader, initialState);

                    if (!initialState)
                    {
                        var sabotagedEvent = new PlayerSabotagedEvent(Game, type);
                        await _eventManager.CallAsync(sabotagedEvent);
                        if (sabotagedEvent.IsCancelled)
                        {
                            _ = Task.Delay(100).ContinueWith(t => FixSabotage(type));
                            reader.RemoveMessage(messageReader);
                        }
                    }
                }
            }
        }

        private async ValueTask FixSabotage(SystemTypes type)
        {
            using var writer = MessageWriter.Get(MessageType.Reliable);
            writer.StartMessage(MessageFlags.GameData);
            writer.Write(Game.Code);
            writer.StartMessage(GameDataTag.DataFlag);
            writer.WritePacked(Game.GameNet.ShipStatus.NetId);

            writer.StartMessage((byte)type);
            switch (type)
            {
                case SystemTypes.Comms:
                {
                    switch (Game.Options.Map)
                    {
                        case MapTypes.Skeld:
                        case MapTypes.Polus:
                        {
                            var hudSystem = _systems[SystemTypes.Comms] as HudOverrideSystemType;
                            hudSystem.IsActive = false;
                            hudSystem.Serialize(writer, false);
                            break;
                        }

                        case MapTypes.MiraHQ:
                        {
                            var hqHudSystem = _systems[SystemTypes.Comms] as HqHudSystemType;
                            hqHudSystem.IsActive = false;
                            hqHudSystem.OpenConsoles.Clear();
                            hqHudSystem.OpenConsoles.Add(new Tuple<byte, byte>(0, 0));
                            hqHudSystem.OpenConsoles.Add(new Tuple<byte, byte>(1, 1));
                            hqHudSystem.CompletedConsoles.Clear();
                            hqHudSystem.CompletedConsoles.Add(0);
                            hqHudSystem.CompletedConsoles.Add(1);
                            hqHudSystem.Serialize(writer, false);
                            break;
                        }
                    }

                    break;
                }

                case SystemTypes.Electrical:
                {
                    var switchSystem = _systems[SystemTypes.Electrical] as SwitchSystem;
                    switchSystem.ActualSwitches = switchSystem.ExpectedSwitches;
                    switchSystem.Value = byte.MaxValue;
                    switchSystem.Serialize(writer, false);
                    break;
                }

                case SystemTypes.Laboratory:
                {
                    var reactorSystem = _systems[SystemTypes.Laboratory] as ReactorSystemType;
                    reactorSystem.Countdown = 10000f;
                    reactorSystem.UserConsolePairs.Clear();
                    reactorSystem.UserConsolePairs.Add(new Tuple<byte, byte>(0, 0));
                    reactorSystem.UserConsolePairs.Add(new Tuple<byte, byte>(1, 1));
                    reactorSystem.Serialize(writer, false);
                    break;
                }

                case SystemTypes.Reactor:
                {
                    if (Game.Options.Map == MapTypes.Airship)
                    {
                        var heliSystemType = _systems[SystemTypes.Reactor] as HeliSabotageSystemType;
                        if (!heliSystemType.IsActive)
                            return;
                        heliSystemType.Countdown = 10000f;
                        heliSystemType.Timer = 10f;
                        heliSystemType.ActiveConsoles.Clear();
                        heliSystemType.ActiveConsoles.Add(new Tuple<byte, byte>(0, 0));
                        heliSystemType.ActiveConsoles.Add(new Tuple<byte, byte>(1, 1));
                        heliSystemType.CompletedConsoles.Clear();
                        heliSystemType.CompletedConsoles.Add(0);
                        heliSystemType.CompletedConsoles.Add(1);
                        heliSystemType.Serialize(writer, false);
                        break;
                    }

                    var reactorSystem = _systems[SystemTypes.Reactor] as ReactorSystemType;
                    reactorSystem.Countdown = 10000f;
                    reactorSystem.UserConsolePairs.Clear();
                    reactorSystem.UserConsolePairs.Add(new Tuple<byte, byte>(0, 0));
                    reactorSystem.UserConsolePairs.Add(new Tuple<byte, byte>(1, 1));
                    reactorSystem.Serialize(writer, false);
                    break;
                }

                case SystemTypes.LifeSupp:
                {
                    var o2System = _systems[SystemTypes.LifeSupp] as LifeSuppSystemType;
                    o2System.Countdown = 10000f;
                    o2System.CompletedConsoles.Clear();
                    o2System.CompletedConsoles.Add(0);
                    o2System.CompletedConsoles.Add(1);
                    o2System.Serialize(writer, false);
                    break;
                }

                case SystemTypes.Sabotage:
                {
                    // ignore
                    return;
                }

                default:
                {
                    return; // don't send the empty message
                }
            }

            writer.EndMessage();
            writer.EndMessage();
            writer.EndMessage();

            await Game.SendToAsync(writer, Game.HostId);
        }

        public override async ValueTask<bool> HandleRpcAsync(ClientPlayer sender, ClientPlayer? target, RpcCalls call, IMessageReader reader)
        {
            if (!await ValidateCmd(call, sender, target))
            {
                return false;
            }

            switch (call)
            {
                case RpcCalls.CloseDoorsOfType:
                {
                    if (!await ValidateImpostor(call, sender, sender.Character!.PlayerInfo))
                    {
                        return false;
                    }

                    Rpc27CloseDoorsOfType.Deserialize(reader, out var systemType);
                    break;
                }

                case RpcCalls.RepairSystem:
                {
                    Rpc28RepairSystem.Deserialize(reader, Game, out var systemType, out var player, out var amount);

                    if (systemType == SystemTypes.Sabotage && !await ValidateImpostor(call, sender, sender.Character!.PlayerInfo))
                    {
                        return false;
                    }

                    break;
                }

                case RpcCalls.UpdateSystem:
                {
                    Rpc35UpdateSystem.Deserialize(reader, Game, out var systemType, out var playerControl, out var sequenceId, out var state, out var ventId);
                    break;
                }

                default:
                    return await base.HandleRpcAsync(sender, target, call, reader);
            }

            return true;
        }

        public virtual Vector2 GetSpawnLocation(IInnerPlayerControl player, int numPlayers, bool initialSpawn)
        {
            var vector = new Vector2(0, 1);
            vector = Rotate(vector, (player.PlayerId - 1) * (360f / numPlayers));
            vector *= this.SpawnRadius;
            return (initialSpawn ? this.InitialSpawnCenter : this.MeetingSpawnCenter) + vector + new Vector2(0f, 0.3636f);
        }

        protected virtual void AddSystems(Dictionary<SystemTypes, ISystemType> systems)
        {
            systems.Add(SystemTypes.Electrical, new SwitchSystem());
            systems.Add(SystemTypes.MedBay, new MedScanSystem());
        }

        private static Vector2 Rotate(Vector2 self, float degrees)
        {
            var f = 0.017453292f * degrees;
            var cos = MathF.Cos(f);
            var sin = MathF.Sin(f);

            return new Vector2((self.X * cos) - (sin * self.Y), (self.X * sin) + (cos * self.Y));
        }
    }
}
