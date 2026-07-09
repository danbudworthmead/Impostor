using System.Linq;
using Impostor.Api.Net.Manager;
using Microsoft.AspNetCore.Mvc;

namespace Impostor.Server.Http;

/// <summary>
/// This controller has a method to get the player count.
/// </summary>
[Route("/api/playercount")]
[ApiController]
public sealed class PlayerCountController : ControllerBase
{
    private readonly IClientManager _clientManager;

    public PlayerCountController(IClientManager clientManager)
    {
        _clientManager = clientManager;
    }

    /// <summary>
    /// Get the player count.
    /// </summary>
    /// <returns>The player count.</returns>
    [HttpGet]
    public IActionResult GetPlayerCount()
    {
        // get all the connected clients
        return Ok(_clientManager.Clients.Count());
    }
}
