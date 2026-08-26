using System.Threading.Tasks;
using Impostor.Api.Innersloth;

namespace Impostor.Server.Net
{
    /// <summary>
    ///     A client that exists only inside the server, with nobody on the other end of it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Used for the two things that need to look like a player without being one: the server
    ///     itself when it holds the host seat, and any character the server puppets.
    ///     </para>
    ///     <para>
    ///     Nothing is ever sent to it. The broadcast helpers filter on
    ///     <see cref="Impostor.Api.Net.IHazelConnection.IsConnected" /> and skip null connections,
    ///     so it is invisible on the wire while still occupying a client id that the rest of the
    ///     server can refer to.
    ///     </para>
    /// </remarks>
    internal sealed class VirtualClient : ClientBase
    {
        /// <summary>
        ///     Name shown for the server itself. Kept within the ten character limit that
        ///     <see cref="Manager.ClientManager" /> enforces for real players.
        /// </summary>
        public const string HostName = "skeld.net";

        public VirtualClient(int id, string name, GameVersion gameVersion)
            : base(
                name,
                gameVersion,
                Language.English,
                QuickChatModes.FreeChatOrQuickChat,
                new PlatformSpecificData(Platforms.Unknown, name),
                connection: null)
        {
            Id = id;
        }

        /// <inheritdoc />
        /// <remarks>Nothing can arrive from a client that has no connection.</remarks>
        public override ValueTask HandleMessageAsync(IMessageReader message, MessageType messageType)
        {
            return default;
        }

        /// <inheritdoc />
        /// <remarks>A virtual client never disconnects; the game is torn down instead.</remarks>
        public override ValueTask HandleDisconnectAsync(string reason)
        {
            return default;
        }
    }
}
