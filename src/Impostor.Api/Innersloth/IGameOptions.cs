using Impostor.Api.Net.Messages;

namespace Impostor.Api.Innersloth
{
    public interface IGameOptions
    {
        /// <summary>
        ///     Gets the version of these GameOptions.
        /// </summary>
        public byte Version { get; }

        /// <summary>
        ///     Gets the currently active gamemode.
        /// </summary>
        public GameModes GameMode { get; }

        /// <summary>
        ///     Gets or sets the maximum amount of players for this lobby.
        /// </summary>
        public byte MaxPlayers { get; set; }

        /// <summary>
        ///     Gets or sets the language of the lobby as per <see cref="GameKeywords" /> enum.
        /// </summary>
        public GameKeywords Keywords { get; set; }

        /// <summary>
        ///     Gets or sets the Map selected for this lobby.
        /// </summary>
        public MapTypes Map { get; set; }

        /// <summary>
        ///     Gets or sets the number of impostors for this lobby.
        /// </summary>
        public int NumImpostors { get; set; }

        /// <summary>
        ///     Gets or sets a value indicating whether the GameOptions are the default ones.
        /// </summary>
        public bool IsDefaults { get; set; }

        public void Deserialize(IMessageReader reader, byte version);
    }
}
