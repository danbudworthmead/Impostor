using System.Linq;
using System.Net.Http.Headers;
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
    /// Initializes a new instance of the <see cref="GamesController"/> class.
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
    /// Get a list of active games.
    /// </summary>
    /// <param name="mapId">Maps that are requested.</param>
    /// <param name="lang">Preferred chat language.</param>
    /// <param name="numImpostors">Amount of impostors. 0 is any.</param>
    /// <param name="authorization">Authorization header containing the matchmaking token.</param>
    /// <returns>An array of game listings.</returns>
    [HttpGet]
    public IActionResult Index(int mapId, GameKeywords lang, int numImpostors, [FromHeader] AuthenticationHeaderValue authorization)
    {
        var listings = _listingManager.FindListings(HttpContext, mapId, numImpostors, lang, null);
        return Ok(listings.Where(g => g.GameState == GameStates.NotStarted).Select(g => g.Code));
    }
}
