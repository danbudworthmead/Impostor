using System;
using Impostor.Api.Events.Player;
using Impostor.Api.Games;
using Impostor.Api.Net;
using Impostor.Api.Net.Inner.Objects;

namespace Impostor.Server.Events.Player;

public class PlayerUnregisteredNetIdEvent : IPlayerUnregisteredNetIdEvent
{
    public PlayerUnregisteredNetIdEvent(IGame game, IClientPlayer clientPlayer, IInnerPlayerControl playerControl, byte tag, uint netId, ReadOnlyMemory<byte> data)
    {
        this.Game = game;
        this.ClientPlayer = clientPlayer;
        this.PlayerControl = playerControl;
        this.Tag = tag;
        this.NetId = netId;
        this.Data = data;
    }

    public IGame Game { get; }

    public IClientPlayer ClientPlayer { get; }

    public IInnerPlayerControl PlayerControl { get; }

    public byte Tag { get; }

    public uint NetId { get; }

    public ReadOnlyMemory<byte> Data { get; }
}
