using System.Linq;
using Impostor.Api.Config;
using Impostor.Api.Games.Managers;
using Impostor.Api.Innersloth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Impostor.Server.Http;

/// <summary>
/// This controller has method to get a list of public games, join by game and create new games.
/// </summary>
[Route("/api/codes")]
[ApiController]
public sealed class CodesController : ControllerBase
{
    private readonly IGameManager _gameManager;
    private readonly ListingManager _listingManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodesController"/> class.
    /// </summary>
    /// <param name="gameManager">GameManager containing a list of games.</param>
    /// <param name="listingManager">ListingManager responsible for filtering.</param>
    /// <param name="serverConfig">Impostor configuration section containing the public ip address of this server.</param>
    public CodesController(IGameManager gameManager, ListingManager listingManager, IOptions<ServerConfig> serverConfig)
    {
        _gameManager = gameManager;
        _listingManager = listingManager;
    }

    /// <summary>
    /// Get the codes of all public games that have not started yet.
    /// </summary>
    /// <returns>An array of game codes.</returns>
    [HttpGet]
    public IActionResult GetCodes()
    {
        var listings = _listingManager.FindListings(HttpContext, null, null, null, null);
        return Ok(listings.Where(g => g.GameState == GameStates.NotStarted).Select(g => g.Code));
    }
}
