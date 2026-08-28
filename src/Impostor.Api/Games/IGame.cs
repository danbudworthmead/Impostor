using System;
using System.Collections.Generic;
using System.Net;
using System.Numerics;
using System.Threading.Tasks;
using Impostor.Api.Innersloth;
using Impostor.Api.Innersloth.GameOptions;
using Impostor.Api.Net;
using Impostor.Api.Net.Inner;
using Impostor.Api.Net.Inner.Objects;

namespace Impostor.Api.Games
{
    public interface IGame
    {
        IGameOptions Options { get; }

        GameFilterOptions FilterOptions { get; }

        GameCode Code { get; }

        GameStates GameState { get; }

        IGameNet GameNet { get; }

        IEnumerable<IClientPlayer> Players { get; }

        IPEndPoint PublicIp { get; }

        int PlayerCount { get; }

        IClientPlayer? Host { get; }

        bool IsPublic { get; }

        /// <summary>
        ///     Gets or sets display name on game list.
        /// </summary>
        string? DisplayName { get; set; }

        IDictionary<object, object> Items { get; }

        int HostId { get; }

        /// <summary>
        /// Gets a value indicating whether the Host of the game has requested host authority.
        /// </summary>
        /// <remarks>
        /// Vanilla Among Us does not request this, but certain client-side mods will.
        /// </remarks>
        bool IsHostAuthoritive { get; }

        /// <summary>
        /// Gets a value indicating whether the server itself holds the host seat of this game,
        /// rather than the player who created it.
        /// </summary>
        /// <remarks>
        /// Experimental. When true, <see cref="Host" /> refers to a client that exists only
        /// inside the server and has no connection, so it must never be sent to or disconnected.
        /// </remarks>
        bool IsServerHosted { get; }

        /// <summary>
        /// Gets the mod GUID that the game was registered with through the AMCI.
        /// </summary>
        Guid? ModGuid { get; }

        /// <summary>
        /// Adds a character to the lobby that no client is behind, visible to everyone but
        /// controlled by nobody.
        /// </summary>
        /// <param name="name">Name to show above the character.</param>
        /// <param name="position">
        /// Where to stand it. When omitted the client seats it as it would any arriving player,
        /// using lobby spawn positions the server has no knowledge of.
        /// </param>
        /// <returns>The player that was added, or null if one could not be.</returns>
        /// <remarks>
        /// Only works while <see cref="IsServerHosted" /> is true and the game has not started,
        /// because a real host client would immediately fight the server over the character.
        /// </remarks>
        ValueTask<IClientPlayer?> SpawnFakePlayerAsync(string name, Vector2? position = null);

        /// <summary>
        /// Begins the game without waiting for a host client to ask for it.
        /// </summary>
        /// <returns>A <see cref="ValueTask" /> representing the asynchronous operation.</returns>
        /// <remarks>
        /// Only does anything while <see cref="IsServerHosted" /> is true and the game has not
        /// started. Any other lobby is begun by its host client instead.
        /// </remarks>
        ValueTask StartAsync();

        /// <summary>
        /// Replaces a player's character with a freshly spawned one in the same spot, dropping
        /// whatever the old one had accumulated - its dead/ghost state very much included.
        /// </summary>
        /// <param name="player">The player whose character should be replaced.</param>
        /// <returns>
        /// The new character, or null if <paramref name="player" /> had none to replace (for
        /// instance because it has not spawned yet, or already left).
        /// </returns>
        /// <remarks>
        /// A client accepts a live (non-ghost) role for a character at most once, ever - see
        /// <see cref="IInnerPlayerControl.SetRoleAsync" />'s remarks - so a murdered player's
        /// existing character can never be turned into a working impostor, or anything else,
        /// after the fact. A brand new character has not had that one chance used up yet.
        /// </remarks>
        ValueTask<IInnerPlayerControl?> RespawnCharacterAsync(IClientPlayer player);

        /// <summary>
        /// Marks the round over and returns the lobby to a joinable state: despawns every
        /// <c>PlayerInfo</c>, puts every player in the limbo a fresh join expects, and raises
        /// the game-ended event. Safe to call more than once for the same round - every call
        /// after the first is a no-op.
        /// </summary>
        /// <param name="gameOverReason">Reason recorded on the game-ended event.</param>
        /// <remarks>
        /// This does not itself tell clients the round ended - a real host's own EndGame
        /// request already carries that message, and a server-hosted game has no host client to
        /// carry it, so callers there send whatever they need to players themselves (which,
        /// notably, can differ per recipient - the vanilla protocol has no way to broadcast a
        /// win to one team and a loss to another in a single message). Call this once whatever
        /// you needed to tell clients has been sent, so a rejoin after the round is over is not
        /// rejected as joining a game that already started.
        /// </remarks>
        ValueTask EndGameAsync(GameOverReason gameOverReason);

        IClientPlayer? GetClientPlayer(int clientId);

        T? FindObjectByNetId<T>(uint netId)
            where T : IInnerNetObject;

        /// <summary>
        ///     Adds an <see cref="IPAddress" /> to the ban list of this game.
        ///     Prevents all future joins from this <see cref="IPAddress" />.
        ///     This does not kick the player with that <see cref="IPAddress" /> from the lobby.
        /// </summary>
        /// <param name="ipAddress">
        ///     The <see cref="IPAddress" /> to ban.
        /// </param>
        void BanIp(IPAddress ipAddress);

        /// <summary>
        ///     Syncs the internal <see cref="IGameOptions" /> to all players.
        ///     Necessary to do if you modified it, otherwise it won't be used.
        /// </summary>
        /// <returns>A <see cref="ValueTask" /> representing the asynchronous operation.</returns>
        ValueTask SyncSettingsAsync();

        /// <summary>
        ///     Sets game's privacy.
        /// </summary>
        /// <param name="isPublic">Privacy to set.</param>
        /// <returns>A <see cref="ValueTask" /> representing the asynchronous operation.</returns>
        ValueTask SetPrivacyAsync(bool isPublic);

        /// <summary>
        ///     Send the message to all players.
        /// </summary>
        /// <param name="writer">Message to send.</param>
        /// <param name="states">Required limbo state of the player.</param>
        /// <returns>A <see cref="ValueTask" /> representing the asynchronous operation.</returns>
        ValueTask SendToAllAsync(IMessageWriter writer, LimboStates states = LimboStates.NotLimbo);

        /// <summary>
        ///     Send the message to all players except one.
        /// </summary>
        /// <param name="writer">Message to send.</param>
        /// <param name="senderId">The player to exclude from sending the message.</param>
        /// <param name="states">Required limbo state of the player.</param>
        /// <returns>A <see cref="ValueTask" /> representing the asynchronous operation.</returns>
        ValueTask SendToAllExceptAsync(IMessageWriter writer, int senderId, LimboStates states = LimboStates.NotLimbo);

        /// <summary>
        ///     Send a message to a specific player.
        /// </summary>
        /// <param name="writer">Message to send.</param>
        /// <param name="id">ID of the client.</param>
        /// <returns>A <see cref="ValueTask" /> representing the asynchronous operation.</returns>
        ValueTask SendToAsync(IMessageWriter writer, int id);

        /// <summary>
        ///     Start a GameData(To) message.
        /// </summary>
        /// <remarks>Use with caution, recommended only for advanced developers.</remarks>
        /// <param name="targetClientId">The client to target if needed, `null` if the message should be sent to all players.</param>
        /// <param name="type">Message type of the message.</param>
        /// <returns>A <see cref="IMessageWriter" /> that you can fill and send using <see cref="FinishGameDataAsync"/>.</returns>
        IMessageWriter StartGameData(int? targetClientId = null, MessageType type = MessageType.Reliable);

        /// <summary>
        ///     Finishes GameData message and sends it to either the target or all players.
        /// </summary>
        /// <remarks>Use with caution, recommended only for advanced developers.</remarks>
        /// <param name="writer">MessageWriter received from <see cref="StartGameData"/>.</param>
        /// <param name="targetClientId">Same target ClientId passed to StartGameData.</param>
        /// <returns>Task that sends the packet.</returns>
        ValueTask FinishGameDataAsync(IMessageWriter writer, int? targetClientId = null);

        /// <summary>
        ///     Creates a MessageWriter with GameData header.
        /// </summary>
        /// <remarks>Use with caution, recommended only for advanced developers.</remarks>
        /// <param name="targetNetId">Net id of the InnerNetObject.</param>
        /// <param name="callId">Rpc id of the message.</param>
        /// <param name="targetClientId">The client to target if needed, `null` if the message should be sent to all players.</param>
        /// <param name="type">Message type of the message.</param>
        /// <returns>A <see cref="IMessageWriter" /> that you can fill with rpc data and send using <see cref="FinishRpcAsync"/>.</returns>
        IMessageWriter StartRpc(uint targetNetId, RpcCalls callId, int? targetClientId = null, MessageType type = MessageType.Reliable);

        /// <summary>
        /// Finishes rpc message and sends it to either the target or all players.
        /// </summary>
        /// <remarks>Use with caution, recommended only for advanced developers.</remarks>
        /// <param name="writer">Message writer of the rpc.</param>
        /// <param name="targetClientId">Same target client id passed to <see cref="StartRpc"/>.</param>
        /// <returns>A <see cref="ValueTask" /> representing the asynchronous operation.</returns>
        ValueTask FinishRpcAsync(IMessageWriter writer, int? targetClientId = null);
    }
}
