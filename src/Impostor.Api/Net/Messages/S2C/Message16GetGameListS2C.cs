using System;
using System.Collections.Generic;
using Impostor.Api.Games;

namespace Impostor.Api.Net.Messages.S2C
{
    public class Message16GetGameListS2C
    {
        public static void Serialize(IMessageWriter writer, IEnumerable<IGame> games)
        {
            writer.StartMessage(MessageFlags.GetGameListV2);

            // Listing
            writer.StartMessage(0);

            foreach (var game in games)
            {
                writer.StartMessage(0);
                writer.Write(game.PublicIp.Address);
                writer.Write((ushort)game.PublicIp.Port);
                game.Code.Serialize(writer);
                writer.Write(game.DisplayName ?? game.Host?.Client.Name ?? string.Empty);
                writer.Write((byte)game.PlayerCount);
                writer.WritePacked(1); // TODO: What does Age do?
                writer.Write((byte)game.Options.CurrentGameOptions.Map);
                writer.Write((byte)game.Options.CurrentGameOptions.NumImpostors);
                writer.Write((byte)game.Options.CurrentGameOptions.MaxPlayers);
                var platform = game.Host?.Client.PlatformSpecificData;
                writer.Write((byte)(platform?.Platform ?? 0));
                writer.Write(platform?.PlatformName ?? string.Empty);
                writer.Write((uint)game.Options.CurrentGameOptions.Keywords);
                writer.EndMessage();
            }

            // WriteLine(writer, "Visit");
            // WriteLine(writer, "www.skeld.net/codes");
            // WriteLine(writer, "to join games");

            writer.EndMessage();
            writer.EndMessage();
        }

        public static void WriteLine(IMessageWriter writer, string text)
        {
            writer.StartMessage(0);
            writer.Write(0);
            writer.Write((ushort)0);
            writer.Write(0);
            writer.Write($"<size=70%><color=orange>{text}");
            writer.Write((byte)0);
            writer.WritePacked(1); // TODO: What does Age do?
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write(string.Empty);
            writer.EndMessage();
        }

        public static void Deserialize(IMessageReader reader)
        {
            throw new NotImplementedException();
        }
    }
}
