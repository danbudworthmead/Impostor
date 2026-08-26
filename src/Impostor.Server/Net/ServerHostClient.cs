using System.Threading.Tasks;
using Impostor.Api.Innersloth;

namespace Impostor.Server.Net
{
    /// <summary>
    ///     A client that exists only inside the server, so that the server itself can hold the
    ///     host seat of a game instead of one of the players.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Among Us gives the host a great deal of authority: it arbitrates most player actions
    ///     through the Cmd -&gt; Check -&gt; Rpc pattern, runs the meeting timer, assigns roles and
    ///     drives game start. Normally that role belongs to whichever player created the lobby,
    ///     which means the server can only validate their decisions after the fact.
    ///     </para>
    ///     <para>
    ///     This client has no connection. Nothing is ever sent to it: the broadcast helpers filter
    ///     on <see cref="Impostor.Api.Net.IHazelConnection.IsConnected" /> and skip null
    ///     connections, so it is invisible to the wire while still occupying a client id that
    ///     <see cref="State.Game.HostId" /> can point at.
    ///     </para>
    /// </remarks>
    internal sealed class ServerHostClient : ClientBase
    {
        /// <summary>
        ///     Name shown for the server in any host-facing UI. Kept within the ten character
        ///     limit that <see cref="Manager.ClientManager" /> enforces for real players.
        /// </summary>
        public const string HostName = "skeld.net";

        public ServerHostClient(int id, GameVersion gameVersion)
            : base(
                HostName,
                gameVersion,
                Language.English,
                QuickChatModes.FreeChatOrQuickChat,
                new PlatformSpecificData(Platforms.Unknown, HostName),
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
        /// <remarks>The server host never disconnects; the game is torn down instead.</remarks>
        public override ValueTask HandleDisconnectAsync(string reason)
        {
            return default;
        }
    }
}
