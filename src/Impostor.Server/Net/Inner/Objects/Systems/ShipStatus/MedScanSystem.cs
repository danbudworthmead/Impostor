using System.Collections.Generic;

namespace Impostor.Server.Net.Inner.Objects.Systems.ShipStatus
{
    public class MedScanSystem : ISystemType
    {
        public MedScanSystem()
        {
            UsersList = new List<byte>();
        }

        public List<byte> UsersList { get; }

        public void Serialize(IMessageWriter writer, bool initialState)
        {
            writer.WritePacked(UsersList.Count);

            foreach (var user in UsersList)
            {
                writer.Write(user);
            }
        }

        public void Deserialize(IMessageReader reader, bool initialState)
        {
            UsersList.Clear();

            var num = reader.ReadPackedInt32();

            for (var i = 0; i < num; i++)
            {
                UsersList.Add(reader.ReadByte());
            }
        }
    }
}
