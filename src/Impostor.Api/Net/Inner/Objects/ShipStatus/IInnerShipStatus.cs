using System.Numerics;

namespace Impostor.Api.Net.Inner.Objects.ShipStatus
{
    public interface IInnerShipStatus : IInnerNetObject
    {
        Vector2 GetSpawnLocation(IInnerPlayerControl player, int numPlayers, bool initialSpawn);
    }
}
