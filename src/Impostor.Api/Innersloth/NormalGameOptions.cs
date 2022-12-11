using Impostor.Api.Net.Messages;

namespace Impostor.Api.Innersloth
{
    public class NormalGameOptions : IGameOptions
    {
        /// <summary>
        ///     Gets or sets the Player speed modifier.
        /// </summary>
        public float PlayerSpeedMod { get; set; } = 1f;

        /// <summary>
        ///     Gets or sets the Light modifier for the players that are members of the crew as a multiplier value.
        /// </summary>
        public float CrewLightMod { get; set; } = 1f;

        /// <summary>
        ///     Gets or sets the Light modifier for the players that are Impostors as a multiplier value.
        /// </summary>
        public float ImpostorLightMod { get; set; } = 1f;

        /// <summary>
        ///     Gets or sets the Impostor cooldown to kill in seconds.
        /// </summary>
        public float KillCooldown { get; set; } = 15f;

        /// <summary>
        ///     Gets or sets the number of common tasks.
        /// </summary>
        public int NumCommonTasks { get; set; } = 1;

        /// <summary>
        ///     Gets or sets the number of long tasks.
        /// </summary>
        public int NumLongTasks { get; set; } = 1;

        /// <summary>
        ///     Gets or sets the number of short tasks.
        /// </summary>
        public int NumShortTasks { get; set; } = 2;

        /// <summary>
        ///     Gets or sets the maximum amount of emergency meetings each player can call during the game in seconds.
        /// </summary>
        public int NumEmergencyMeetings { get; set; } = 1;

        /// <summary>
        ///     Gets or sets the cooldown between each time any player can call an emergency meeting in seconds.
        /// </summary>
        public int EmergencyCooldown { get; set; } = 15;

        /// <summary>
        ///     Gets or sets a value indicating whether ghosts (dead crew members) can do tasks.
        /// </summary>
        public bool GhostsDoTasks { get; set; } = true;

        /// <summary>
        ///     Gets or sets the Kill as per values in <see cref="KillDistances" />.
        /// </summary>
        public KillDistances KillDistance { get; set; } = KillDistances.Normal;

        /// <summary>
        ///     Gets or sets the time for discussion before voting time in seconds.
        /// </summary>
        public int DiscussionTime { get; set; } = 15;

        /// <summary>
        ///     Gets or sets the time for voting in seconds.
        /// </summary>
        public int VotingTime { get; set; } = 120;

        /// <summary>
        ///     Gets or sets a value indicating whether an ejected player is an impostor or not.
        /// </summary>
        public bool ConfirmImpostor { get; set; } = true;

        /// <summary>
        ///     Gets or sets a value indicating whether players are able to see tasks being performed by other players.
        /// </summary>
        /// <remarks>
        ///     By being set to true, tasks such as Empty Garbage, Submit Scan, Clear asteroids, Prime shields execution will be visible to other players.
        /// </remarks>
        public bool VisualTasks { get; set; } = true;

        /// <summary>
        ///     Gets or sets a value indicating whether the vote is anonymous.
        /// </summary>
        public bool AnonymousVotes { get; set; }

        /// <summary>
        ///     Gets or sets the task bar update mode as per values in <see cref="Innersloth.TaskBarUpdate" />.
        /// </summary>
        public TaskBarUpdate TaskBarUpdate { get; set; } = TaskBarUpdate.Always;

        public byte Version { get; private set; } = 0;

        public GameModes GameMode => GameModes.Normal;

        public byte MaxPlayers { get; set; } = 10;

        public GameKeywords Keywords { get; set; } = GameKeywords.English;

        public MapTypes Map { get; set; } = MapTypes.Skeld;

        public int NumImpostors { get; set; } = 2;

        public RoleOptionsCollection RoleOptions { get; set; } = new RoleOptionsCollection();

        public bool IsDefaults { get;  set; } = true;

        public void Deserialize(IMessageReader reader, byte version)
        {
            Version = version;

            if (version > 7)
            {
                throw new ImpostorException($"Unknown GameOptionsData version {version}.");
            }

            MaxPlayers = reader.ReadByte();
            Keywords = (GameKeywords)reader.ReadUInt32();
            Map = (MapTypes)reader.ReadByte();
            PlayerSpeedMod = reader.ReadSingle();

            CrewLightMod = reader.ReadSingle();
            ImpostorLightMod = reader.ReadSingle();
            KillCooldown = reader.ReadSingle();

            NumCommonTasks = reader.ReadByte();
            NumLongTasks = reader.ReadByte();
            NumShortTasks = reader.ReadByte();

            NumEmergencyMeetings = reader.ReadInt32();

            NumImpostors = reader.ReadByte();
            KillDistance = (KillDistances)reader.ReadByte();
            DiscussionTime = reader.ReadInt32();
            VotingTime = reader.ReadInt32();

            IsDefaults = reader.ReadBoolean();

            EmergencyCooldown = reader.ReadByte();
            ConfirmImpostor = reader.ReadBoolean();
            VisualTasks = reader.ReadBoolean();
            AnonymousVotes = reader.ReadBoolean();
            TaskBarUpdate = (TaskBarUpdate)reader.ReadByte();

            RoleOptions = RoleOptionsCollection.Deserialize(reader);
        }
    }
}
