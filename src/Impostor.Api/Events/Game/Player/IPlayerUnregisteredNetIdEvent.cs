using System;

namespace Impostor.Api.Events.Player
{
    /// <summary>
    ///     Fired when a client sends a GameData message (<c>DataFlag</c> or <c>RpcFlag</c>) that targets a
    ///     NetId that isn't currently registered to any spawned InnerNetObject.
    ///     <para>
    ///         This can legitimately happen due to race conditions (e.g. a just-despawned object), but it is
    ///         also a common signature of cheat clients that try to smuggle fake/garbage sub-messages past
    ///         naive anti-cheat implementations that only look at the first flag of a batch.
    ///     </para>
    /// </summary>
    public interface IPlayerUnregisteredNetIdEvent : IPlayerEvent
    {
        /// <summary>
        ///     Gets the raw GameData tag that was received (DataFlag = 1, RpcFlag = 2).
        /// </summary>
        byte Tag { get; }

        /// <summary>
        ///     Gets the NetId that could not be resolved to a spawned object.
        /// </summary>
        uint NetId { get; }

        /// <summary>
        ///     Gets the raw bytes of the sub-message payload that followed the NetId, if any.
        /// </summary>
        ReadOnlyMemory<byte> Data { get; }
    }
}
