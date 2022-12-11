using System.Collections.Generic;
using Impostor.Api.Net.Messages;
using static Impostor.Api.Innersloth.RoleOptionsData;

namespace Impostor.Api.Innersloth
{
    public class RoleOptionsCollection
    {
        public Dictionary<RoleTypes, RoleData> Roles { get; } = new Dictionary<RoleTypes, RoleData>();

        public static RoleOptionsCollection Deserialize(IMessageReader reader)
        {
            var collection = new RoleOptionsCollection();

            var num = reader.ReadPackedInt32();

            for (var i = 0; i < num; i++)
            {
                var roleType = (RoleTypes)reader.ReadInt16();
                var maxCount = reader.ReadSByte();
                var chance = reader.ReadSByte();

                // TODO deserialize these options
                var roleOptsReader = reader.ReadMessage();

                collection.Roles.Add(roleType, new RoleData(roleType, maxCount, chance));
            }

            return collection;
        }

        public readonly struct RoleData
        {
            public readonly RoleTypes Type;

            public readonly RoleRate Rate;

            // public IRoleOptions roleOptions; // TODO
            public RoleData(RoleTypes type, int maxCount, int chance)
            {
                Type = type;
                Rate = new RoleRate(maxCount, chance);
            }
        }
    }
}
