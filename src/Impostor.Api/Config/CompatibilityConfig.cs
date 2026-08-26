namespace Impostor.Api.Config
{
    public class CompatibilityConfig
    {
        public const string Section = "Compatibility";

        public bool AllowFutureGameVersions { get; set; } = false;

        public bool AllowHostAuthority { get; set; } = false;

        public bool AllowVersionMixing { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether the server takes the host seat of a game
        /// instead of the player who created it.
        /// </summary>
        /// <remarks>
        /// Experimental and incomplete. The host is responsible for role assignment, the meeting
        /// timer, sabotage systems and win conditions, so a game hosted by the server will not
        /// play correctly until the server implements all of them. Off by default; games only
        /// opt in individually, see IGame.IsServerHosted.
        /// </remarks>
        public bool AllowServerAsHost { get; set; } = true;
    }
}
