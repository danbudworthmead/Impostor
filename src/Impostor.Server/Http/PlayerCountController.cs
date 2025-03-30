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
    IClientManager _iClientManager;

    public PlayerCountController(IClientManager iClientManager)
    {
        _iClientManager = iClientManager;
    }

    /// <summary>
    /// Get the player count.
    /// </summary>
    /// <returns>The player count.</returns>
    [HttpGet]
    public IActionResult GetPlayerCount()
    {
        // get all the connected clients
        return Ok(_iClientManager.Clients.Count());
    }
}
