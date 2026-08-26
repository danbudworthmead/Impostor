using System.Threading.Tasks;
using Impostor.Api.Net.Inner;

namespace Impostor.Server.Net.Inner.Objects.GameManager.Logic;

internal abstract class GameLogicComponent
{
    public virtual ValueTask<bool> HandleRpcAsync(RpcCalls callId, IMessageReader reader)
    {
        return ValueTask.FromResult(false);
    }

    public virtual ValueTask<bool> SerializeAsync(IMessageWriter writer, bool initialState)
    {
        // Most logic components carry no spawn state; their counterparts on the client read
        // nothing. Only the ones that do, such as LogicOptions, override this.
        return ValueTask.FromResult(false);
    }

    public virtual ValueTask DeserializeAsync(IMessageReader reader, bool initialState)
    {
        return default;
    }
}
