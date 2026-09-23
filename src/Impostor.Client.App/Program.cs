using System;
using System.Net;
using System.Threading.Tasks;
using Impostor.Client;
using Serilog;

namespace Impostor.Client.App
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger();

            var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 22023);

            await using var host = new BotClient(endpoint, "BotHost");
            await host.ConnectAsync();
            Log.Information("Host connected.");

            var code = await host.HostGameAsync();
            Log.Information("Hosted game {Code}.", code.Code);

            await using var joiner = new BotClient(endpoint, "BotJoin");
            await joiner.ConnectAsync();
            Log.Information("Joiner connected.");

            await joiner.JoinGameAsync(code);
            Log.Information("Sent join request.");

            await Task.Delay(3000);

            Log.Information(
                "host: clientId={0} characterNetId={1} role={2} disconnect={3}",
                host.ClientId,
                host.CharacterNetId,
                host.Role,
                host.DisconnectReason);

            Log.Information(
                "joiner: clientId={0} characterNetId={1} role={2} disconnect={3}",
                joiner.ClientId,
                joiner.CharacterNetId,
                joiner.Role,
                joiner.DisconnectReason);
        }
    }
}
