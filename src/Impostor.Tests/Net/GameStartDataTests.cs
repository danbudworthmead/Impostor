using System;
using System.Linq;
using Impostor.Api.Innersloth;
using Impostor.Api.Innersloth.Maps;
using Impostor.Api.Net.Messages;
using Impostor.Api.Net.Messages.Rpcs;
using Impostor.Hazel;
using Impostor.Hazel.Abstractions;
using Impostor.Hazel.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;
using Xunit;

namespace Impostor.Tests.Net
{
    /// <summary>
    /// The server deals out tasks at game start by picking from the map's own task list. A client
    /// that receives no tasks never finishes loading and can never move, with nothing logged to
    /// say why, so the pieces that feed that list are worth pinning down.
    /// </summary>
    public class GameStartDataTests
    {
        private static readonly ObjectPool<MessageReader> ReaderPool = BuildReaderPool();

        [Theory]
        [InlineData(MapTypes.Skeld)]
        [InlineData(MapTypes.MiraHQ)]
        [InlineData(MapTypes.Polus)]
        [InlineData(MapTypes.Airship)]
        [InlineData(MapTypes.Fungle)]
        public void EveryMap_HasTasksInEveryCategory(MapTypes map)
        {
            var tasks = MapData.Maps[map].Tasks.Values;

            Assert.NotEmpty(tasks.Where(task => task.Category == TaskCategories.CommonTask));
            Assert.NotEmpty(tasks.Where(task => task.Category == TaskCategories.LongTask));
            Assert.NotEmpty(tasks.Where(task => task.Category == TaskCategories.ShortTask));
        }

        [Fact]
        public void EveryMap_HasTaskIdsThatFitInAByte()
        {
            // Task ids travel as single bytes, so a map with more than 256 of them would need a
            // different wire format rather than a silent truncation.
            foreach (var (map, data) in MapData.Maps)
            {
                Assert.All(data.Tasks.Keys, id => Assert.InRange(id, 0, byte.MaxValue));
                Assert.NotEmpty(data.Tasks);
                Assert.True(data.Tasks.Count > 0, map + " has no tasks");
            }
        }

        [Fact]
        public void SetTasks_RoundTrips()
        {
            var sent = new byte[] { 3, 17, 42, 0 };

            using var writer = MessageWriter.Get(MessageType.Reliable);
            Rpc29SetTasks.Serialize(writer, sent);

            var reader = ReaderPool.Get();
            reader.Update(writer.Buffer.AsSpan(3, writer.Length - 3).ToArray());

            Rpc29SetTasks.Deserialize(reader, out var received);

            Assert.Equal(sent, received.ToArray());
        }

        private static ObjectPool<MessageReader> BuildReaderPool()
        {
            var services = new ServiceCollection();
            services.AddHazel();
            return services.BuildServiceProvider().GetRequiredService<ObjectPool<MessageReader>>();
        }
    }
}
