using System.Collections.Generic;
using Impostor.Api.Events.Managers;
using Impostor.Api.Innersloth;
using Impostor.Api.Net.Custom;
using Impostor.Server.Net.Inner.Objects.Systems;
using Impostor.Server.Net.Inner.Objects.Systems.ShipStatus;
using Impostor.Server.Net.State;

namespace Impostor.Server.Net.Inner.Objects.ShipStatus
{
    internal class InnerFungleShipStatus : InnerShipStatus
    {
        public InnerFungleShipStatus(ICustomMessageManager<ICustomRpc> customMessageManager, Game game, IEventManager eventManager) : base(customMessageManager, game, MapTypes.Fungle, eventManager)
        {
        }

        protected override void AddSystems(Dictionary<SystemTypes, ISystemType> systems)
        {
            base.AddSystems(systems);

            systems.Add(SystemTypes.Comms, new HudOverrideSystemType());
            systems.Add(SystemTypes.Reactor, new ReactorSystemType());
            systems.Add(SystemTypes.Doors, new DoorsSystemType(Doors));
            systems.Add(SystemTypes.MushroomMixupSabotage, new MushroomMixupSabotageSystemType());
        }
    }
}
