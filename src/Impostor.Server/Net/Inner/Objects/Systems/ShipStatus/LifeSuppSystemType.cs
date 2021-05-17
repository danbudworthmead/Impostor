using System;
using System.Collections.Generic;
using System.Linq;
using Impostor.Api.Net.Messages;

namespace Impostor.Server.Net.Inner.Objects.Systems.ShipStatus
{
    public class LifeSuppSystemType : ISystemType, IActivatable
    {
        public LifeSuppSystemType()
        {
            Countdown = 10000f;
            CompletedConsoles = new HashSet<int>();
        }

        public float Countdown { get; set; }

        public HashSet<int> CompletedConsoles { get; }

        public bool IsActive => Countdown < 10000.0;

        public void Serialize(IMessageWriter writer, bool initialState)
        {
            writer.Write(Countdown);
            writer.WritePacked(CompletedConsoles.Count());
            foreach (var completedConsole in CompletedConsoles)
            {
                writer.WritePacked(completedConsole);
            }
        }

        public void Deserialize(IMessageReader reader, bool initialState)
        {
            Countdown = reader.ReadSingle();

            if (reader.Position >= reader.Length)
            {
                return;
            }

            CompletedConsoles.Clear(); // TODO: Thread safety

            var num = reader.ReadPackedInt32();

            for (var i = 0; i < num; i++)
            {
                var val = reader.ReadPackedInt32();
                CompletedConsoles.Add(val);
            }
        }
    }
}
