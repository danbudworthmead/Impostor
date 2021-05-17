using System;
using System.Collections.Generic;
using System.Linq;
using Impostor.Api.Net.Messages;

namespace Impostor.Server.Net.Inner.Objects.Systems.ShipStatus
{
    public class ReactorSystemType : ISystemType, IActivatable
    {
        public ReactorSystemType()
        {
            Countdown = 10000f;
            UserConsolePairs = new HashSet<Tuple<byte, byte>>();
        }

        public float Countdown { get; set; }

        public HashSet<Tuple<byte, byte>> UserConsolePairs { get; }

        public bool IsActive => Countdown < 10000.0;

        public void Serialize(IMessageWriter writer, bool initialState)
        {
            writer.Write(Countdown);
            writer.WritePacked(UserConsolePairs.Count);
            writer.Write(UserConsolePairs.First().Item1);
            writer.Write(UserConsolePairs.First().Item2);
            writer.Write(UserConsolePairs.Last().Item1);
            writer.Write(UserConsolePairs.Last().Item2);
        }

        public void Deserialize(IMessageReader reader, bool initialState)
        {
            Countdown = reader.ReadSingle();
            UserConsolePairs.Clear(); // TODO: Thread safety

            var count = reader.ReadPackedInt32();

            for (var i = 0; i < count; i++)
            {
                UserConsolePairs.Add(new Tuple<byte, byte>(reader.ReadByte(), reader.ReadByte()));
            }
        }
    }
}
