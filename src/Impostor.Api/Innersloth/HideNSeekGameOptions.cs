using Impostor.Api.Net.Messages;

namespace Impostor.Api.Innersloth
{
    public class HideNSeekGameOptions : IGameOptions
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

        public int CrewmateVentUses { get; set; } = 1;

        public float CrewmateTimeInVent { get; set; } = 3f;

        public float HidingTime { get; set; } = 200f;

        public float CrewmateFlashlightSize { get; set; } = 0.35f;

        public float ImpostorFlashlightSize { get; set; } = 0.25f;

        public bool UseFlashlight { get; set; } = true;

        public bool FinalHideSeekMap { get; set; } = true;

        public float FinalHideTime { get; set; } = 50f;

        public float FinalSeekerSpeed { get; set; } = 1.2f;

        public bool FinalHidePings { get; set; } = true;

        public bool ShowNames { get; set; } = true;

        public uint SeekerPlayerId { get; set; } = 0xFFFFFFFF;

        public float MaxPingTime { get; set; } = 6f;

        public byte Version { get; private set; } = 0;

        public GameModes GameMode => GameModes.HideNSeek;

        public byte MaxPlayers { get; set; }

        public GameKeywords Keywords { get; set; }

        public MapTypes Map { get; set; }

        public int NumImpostors { get; set; }

        public bool IsDefaults { get; set; }

        public void Deserialize(IMessageReader reader, byte version)
        {
            Version = version;

            if (version > 7)
            {
                throw new ImpostorException($"Unknown GameOptionsData version {version}.");
            }

            MaxPlayers = reader.ReadByte();
            Keywords = (GameKeywords)reader.ReadInt32();
            Map = (MapTypes)reader.ReadByte();
            PlayerSpeedMod = reader.ReadSingle();
            CrewLightMod = reader.ReadSingle();
            ImpostorLightMod = reader.ReadSingle();
            NumCommonTasks = reader.ReadByte();
            NumLongTasks = reader.ReadByte();
            NumShortTasks = reader.ReadByte();
            IsDefaults = reader.ReadBoolean();

            CrewmateVentUses = reader.ReadInt32();
            HidingTime = reader.ReadSingle();
            CrewmateFlashlightSize = reader.ReadSingle();
            ImpostorFlashlightSize = reader.ReadSingle();
            UseFlashlight = reader.ReadBoolean();
            FinalHideSeekMap = reader.ReadBoolean();
            FinalHideTime = reader.ReadSingle();
            FinalSeekerSpeed = reader.ReadSingle();
            FinalHidePings = reader.ReadBoolean();
            ShowNames = reader.ReadBoolean();
            SeekerPlayerId = reader.ReadUInt32();
            MaxPingTime = reader.ReadSingle();
            CrewmateTimeInVent = reader.ReadSingle();
        }
    }
}
