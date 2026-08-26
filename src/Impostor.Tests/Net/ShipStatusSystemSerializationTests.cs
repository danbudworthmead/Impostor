using System;
using System.Collections.Generic;
using Impostor.Api.Net.Messages;
using Impostor.Hazel;
using Impostor.Hazel.Abstractions;
using Impostor.Hazel.Extensions;
using Impostor.Server.Net.Inner.Objects.Systems;
using Impostor.Server.Net.Inner.Objects.Systems.ShipStatus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;
using Xunit;

namespace Impostor.Tests.Net
{
    /// <summary>
    /// A ship's systems are written into the spawn payload the client reads to build the map, so
    /// a serializer that does not exactly mirror its own reader strands everyone on the loading
    /// screen. These feed each system a payload, have it write itself back out, and require the
    /// two to be identical.
    /// </summary>
    public class ShipStatusSystemSerializationTests
    {
        private static readonly ObjectPool<MessageReader> ReaderPool = BuildReaderPool();

        public static TheoryData<string, ISystemType, byte[]> Systems()
        {
            var doors = new Dictionary<int, bool> { [0] = false, [1] = false, [2] = false };
            var electricalDoors = new Dictionary<int, bool> { [0] = false, [1] = false };

            return new TheoryData<string, ISystemType, byte[]>
            {
                { nameof(SwitchSystem), new SwitchSystem(), new byte[] { 0b10101, 0b01010, 0xFF } },
                { nameof(MedScanSystem), new MedScanSystem(), new byte[] { 2, 3, 7 } },
                { nameof(HudOverrideSystemType), new HudOverrideSystemType(), new byte[] { 1 } },
                { nameof(SecurityCameraSystemType), new SecurityCameraSystemType(), new byte[] { 0 } },
                { nameof(SabotageSystemType), new SabotageSystemType(Array.Empty<IActivatable>()), new byte[] { 0, 0, 0x20, 0x41 } },
                { nameof(ReactorSystemType), new ReactorSystemType(), new byte[] { 0, 0, 0x20, 0x41, 1, 4, 5 } },
                { nameof(LifeSuppSystemType), new LifeSuppSystemType(), new byte[] { 0, 0, 0x20, 0x41, 2, 1, 3 } },
                { nameof(AutoDoorsSystemType), new AutoDoorsSystemType(doors), new byte[] { 1, 0, 1 } },
                { nameof(ElectricalDoors), new ElectricalDoors(electricalDoors), new byte[] { 3, 0, 0, 0 } },
            };
        }

        [Theory]
        [MemberData(nameof(Systems))]
        public void Serialize_WritesBackWhatItRead(string name, ISystemType system, byte[] payload)
        {
            system.Deserialize(Read(payload), true);

            using var writer = MessageWriter.Get(MessageType.Reliable);
            system.Serialize(writer, true);

            Assert.True(payload.AsSpan().SequenceEqual(Written(writer)), name + " did not write back what it read");
        }

        [Fact]
        public void AutoDoors_WhenNotInitial_ReportsNothingDirty()
        {
            var system = new AutoDoorsSystemType(new Dictionary<int, bool> { [0] = true });

            using var writer = MessageWriter.Get(MessageType.Reliable);
            system.Serialize(writer, false);

            // A single packed zero: the server never opens or closes a door by itself.
            Assert.Equal(new byte[] { 0 }, Written(writer));
        }

        private static ObjectPool<MessageReader> BuildReaderPool()
        {
            var services = new ServiceCollection();
            services.AddHazel();
            return services.BuildServiceProvider().GetRequiredService<ObjectPool<MessageReader>>();
        }

        private static IMessageReader Read(byte[] payload)
        {
            var reader = ReaderPool.Get();
            reader.Update(payload);
            return reader;
        }


        private static byte[] Written(IMessageWriter writer)
        {
            // Get(MessageType.Reliable) reserves a header the systems do not write into.
            return writer.Buffer.AsSpan(3, writer.Length - 3).ToArray();
        }
    }
}
