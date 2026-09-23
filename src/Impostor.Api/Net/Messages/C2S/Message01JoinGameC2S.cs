using Impostor.Api.Games;

namespace Impostor.Api.Net.Messages.C2S
{
    public static class Message01JoinGameC2S
    {
        public static void Serialize(IMessageWriter writer, GameCode gameCode)
        {
            writer.StartMessage(MessageFlags.JoinGame);
            gameCode.Serialize(writer);
            writer.Write(false); // no crossplay
            writer.EndMessage();
        }

        public static void Deserialize(IMessageReader reader, out GameCode gameCode)
        {
            gameCode = reader.ReadInt32();
            reader.ReadBoolean(); // no crossplay
        }
    }
}
