using System;

namespace Impostor.Server.Net.Inner.Objects.Systems.ShipStatus
{
    public class HudOverrideSystemType : ISystemType, IActivatable
    {
        public bool IsActive { get; set; }

        public void Serialize(IMessageWriter writer, bool initialState)
        {
            writer.Write(IsActive);
        }

        public void Deserialize(IMessageReader reader, bool initialState)
        {
            IsActive = reader.ReadBoolean();
        }
    }
}
