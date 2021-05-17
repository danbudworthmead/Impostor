using System;
using System.Collections.Generic;
using System.Linq;
using Impostor.Api.Net.Messages;

namespace Impostor.Server.Net.Inner.Objects.Systems.ShipStatus
{
    public class HqHudSystemType : ISystemType, IActivatable
    {
        public HqHudSystemType()
        {
            OpenConsoles = new HashSet<Tuple<byte, byte>>();
            CompletedConsoles = new HashSet<byte>();
        }

        public HashSet<Tuple<byte, byte>> OpenConsoles { get; set; }

        public HashSet<byte> CompletedConsoles { get; set; }

        public bool IsActive { get; set; }

        public void Serialize(IMessageWriter writer, bool initialState)
        {
            writer.WritePacked(OpenConsoles.Count());
            foreach (var openConsole in OpenConsoles)
            {
                writer.Write(openConsole.Item1);
                writer.Write(openConsole.Item2);
            }

            writer.WritePacked(CompletedConsoles.Count);
            foreach (var completedConsole in CompletedConsoles)
            {
                writer.Write(completedConsole);
            }
        }

        public void Deserialize(IMessageReader reader, bool initialState)
        {
            OpenConsoles.Clear();
            var count = reader.ReadPackedUInt32();
            for (var i = 0; i < count; i++)
            {
                OpenConsoles.Add(new Tuple<byte, byte>(reader.ReadByte(), reader.ReadByte()));
            }

            CompletedConsoles.Clear();
            count = reader.ReadPackedUInt32();
            for (var i = 0; i < count; i++)
            {
                CompletedConsoles.Add(reader.ReadByte());
            }
        }
    }
}
