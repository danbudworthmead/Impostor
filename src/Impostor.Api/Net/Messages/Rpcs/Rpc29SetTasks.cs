using System;

namespace Impostor.Api.Net.Messages.Rpcs
{
    public static class Rpc29SetTasks
    {
        public static void Serialize(IMessageWriter writer, ReadOnlyMemory<byte> taskTypeIds)
        {
            // Length prefixed, to match the ReadBytesAndSize below and the client's own reader.
            // Writing the bytes bare costs the first task and leaves the rest misread.
            writer.WriteBytesAndSize(taskTypeIds.ToArray());
        }

        public static void Deserialize(IMessageReader reader, out ReadOnlyMemory<byte> taskTypeIds)
        {
            taskTypeIds = reader.ReadBytesAndSize();
        }
    }
}
