namespace Impostor.Server.Net.Inner.Objects.Systems.ShipStatus
{
    public class SecurityCameraSystemType : ISystemType
    {
        public byte InUse { get; internal set; }

        public void Serialize(IMessageWriter writer, bool initialState)
        {
            // The client writes the number of players watching, then one byte each.
            writer.WritePacked(InUse);
        }

        public void Deserialize(IMessageReader reader, bool initialState)
        {
            InUse = reader.ReadByte();
        }
    }
}
