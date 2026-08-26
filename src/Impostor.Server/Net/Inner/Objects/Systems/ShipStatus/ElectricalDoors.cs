using System.Collections.Generic;

namespace Impostor.Server.Net.Inner.Objects.Systems.ShipStatus
{
    public class ElectricalDoors : ISystemType
    {
        private readonly Dictionary<int, bool> _doors;

        public ElectricalDoors(Dictionary<int, bool> doors)
        {
            _doors = doors;
        }

        public void Serialize(IMessageWriter writer, bool initialState)
        {
            var bits = 0u;

            for (var i = 0; i < _doors.Count; i++)
            {
                if (_doors[i])
                {
                    bits |= 1u << (i & 31);
                }
            }

            writer.Write(bits);
        }

        public void Deserialize(IMessageReader reader, bool initialState)
        {
            var num = reader.ReadUInt32();
            for (var i = 0; i < _doors.Count; i++)
            {
                _doors[i] = (num & (ulong)(1L << (i & 31))) > 0UL;
            }
        }
    }
}
