namespace Impostor.Server.Net.Inner.Objects.Systems.ShipStatus
{
    public class MovingPlatformBehaviour : ISystemType, IActivatable
    {
        /// <summary>
        /// Net id the client writes when the platform is carrying nobody.
        /// </summary>
        private const uint NoRider = uint.MaxValue;

        private byte _useId;
        private uint _rider = NoRider;

        public bool IsActive { get; private set; }

        public bool IsLeft { get; private set; }

        public void Serialize(IMessageWriter writer, bool initialState)
        {
            // Each use carries a sequence number so late or reordered messages can be dropped.
            _useId++;

            writer.Write(_useId);
            writer.Write(_rider);
            writer.Write(IsLeft);
        }

        public void Deserialize(IMessageReader reader, bool initialState)
        {
            _useId = reader.ReadByte();
            _rider = reader.ReadUInt32();
            IsLeft = reader.ReadBoolean();
            IsActive = _rider != NoRider;
        }
    }
}
