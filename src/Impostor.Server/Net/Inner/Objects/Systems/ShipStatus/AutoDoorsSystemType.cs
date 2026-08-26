using System.Collections.Generic;

namespace Impostor.Server.Net.Inner.Objects.Systems.ShipStatus
{
    public class AutoDoorsSystemType : ISystemType
    {
        private readonly Dictionary<int, bool> _doors;

        public AutoDoorsSystemType(Dictionary<int, bool> doors)
        {
            _doors = doors;
        }

        public void Serialize(IMessageWriter writer, bool initialState)
        {
            if (!initialState)
            {
                // The server never opens or closes a door of its own accord, so nothing is dirty.
                writer.WritePacked(0u);
                return;
            }

            for (var i = 0; i < _doors.Count; i++)
            {
                writer.Write(_doors[i]);
            }
        }

        public void Deserialize(IMessageReader reader, bool initialState)
        {
            if (initialState)
            {
                for (var i = 0; i < _doors.Count; i++)
                {
                    _doors[i] = reader.ReadBoolean();
                }
            }
            else
            {
                var num = reader.ReadPackedUInt32();

                for (var i = 0; i < _doors.Count; i++)
                {
                    if ((num & 1 << i) != 0)
                    {
                        _doors[i] = reader.ReadBoolean();
                    }
                }
            }
        }
    }
}
