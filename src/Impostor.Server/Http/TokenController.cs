using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Impostor.Api.Innersloth;
using Impostor.Server.Net.Manager;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Impostor.Server.Http;

/// <summary>
/// This controller has a method to get an auth token.
/// </summary>
[Route("/api/user")]
[ApiController]
public sealed class TokenController : ControllerBase
{
    /// <summary>
    /// Longest product user id we will remember. Real EOS ids are 32 hex characters.
    /// </summary>
    private const int MaxProductUserIdLength = 64;

    /// <summary>
    /// Longest friend code we will remember. Real codes look like <c>word#1234</c>.
    /// </summary>
    private const int MaxFriendCodeLength = 32;

    /// <summary>
    /// How many times to report the shape of the request body. The client's exact payload is
    /// undocumented, so this tells us which fields it actually sends without filling the journal.
    /// </summary>
    private const int UnknownFieldReportLimit = 20;

    private static int _unknownFieldReports;

    private readonly ClientIdentityCache _identityCache;
    private readonly ILogger<TokenController> _logger;

    public TokenController(ClientIdentityCache identityCache, ILogger<TokenController> logger)
    {
        _identityCache = identityCache;
        _logger = logger;
    }

    /// <summary>
    /// Get an authentication token.
    /// </summary>
    /// <param name="request">Token parameters that need to be put into the token.</param>
    /// <returns>A bare minimum authentication token that the client will accept.</returns>
    [HttpPost]
    public IActionResult GetToken([FromBody] TokenRequest request)
    {
        ReportUnknownFields(request);
        RememberIdentity(request);

        var token = new Token
        {
            Content = new TokenPayload
            {
                ProductUserId = request.ProductUserId,
                ClientVersion = request.ClientVersion,
            },
            Hash = "impostor_was_here",
        };

        // Wrap into a Base64 sandwich
        var serialized = JsonSerializer.SerializeToUtf8Bytes(token);
        return this.Ok(Convert.ToBase64String(serialized));
    }

    /// <summary>
    /// Trims a client-supplied string to something safe to re-broadcast and to write to logs.
    /// </summary>
    /// <param name="value">The value the client sent.</param>
    /// <param name="maxLength">Longest value that will be accepted.</param>
    /// <returns>The value, or an empty string when it is missing or implausible.</returns>
    private static string Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maxLength)
        {
            return string.Empty;
        }

        foreach (var character in value)
        {
            if (char.IsControl(character))
            {
                return string.Empty;
            }
        }

        return value;
    }

    /// <summary>
    /// Logs the names of any properties the client sent that we do not model, so we can tell
    /// whether a friend code is available here. Only names are logged, never values: the
    /// payload carries account identifiers.
    /// </summary>
    /// <param name="request">The deserialized request body.</param>
    private void ReportUnknownFields(TokenRequest request)
    {
        if (request.ExtraProperties is not { Count: > 0 })
        {
            return;
        }

        if (Interlocked.Increment(ref _unknownFieldReports) > UnknownFieldReportLimit)
        {
            return;
        }

        _logger.LogInformation(
            "Token request contained unmodelled fields: {Fields}",
            string.Join(", ", request.ExtraProperties.Keys));
    }

    /// <summary>
    /// Stores what the client claims about itself so the UDP connection that follows can be
    /// matched to it. Values are client-supplied and unverified; they are only ever shown to
    /// other players.
    /// </summary>
    /// <param name="request">The deserialized request body.</param>
    private void RememberIdentity(TokenRequest request)
    {
        var address = ResolveCallerAddress();
        if (address == null)
        {
            return;
        }

        var productUserId = Sanitize(request.ProductUserId, MaxProductUserIdLength);
        var friendCode = Sanitize(request.FriendCode, MaxFriendCodeLength);

        if (productUserId.Length == 0 && friendCode.Length == 0)
        {
            return;
        }

        _identityCache.Store(address, request.Username, productUserId, friendCode);
    }

    /// <summary>
    /// Works out which address the game will connect from. Behind nginx every request appears to
    /// come from loopback, so the forwarded headers carry the real address — but they are only
    /// trusted when the immediate peer really is the local proxy. Kestrel can be bound to a public
    /// interface, and an attacker reaching it directly could otherwise plant an identity against
    /// somebody else's address.
    /// </summary>
    /// <returns>The address to key the cached identity on, or <see langword="null" /> if unknown.</returns>
    private IPAddress? ResolveCallerAddress()
    {
        var remote = HttpContext.Connection.RemoteIpAddress;

        if (remote == null || !IPAddress.IsLoopback(remote))
        {
            return remote;
        }

        var forwardedFor = HttpContext.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // May be a chain; the client is the first entry.
            var first = forwardedFor.Split(',').FirstOrDefault()?.Trim();
            if (IPAddress.TryParse(first, out var parsed))
            {
                return parsed;
            }
        }

        var realIp = HttpContext.Request.Headers["X-Real-IP"].ToString();
        if (IPAddress.TryParse(realIp.Trim(), out var realParsed))
        {
            return realParsed;
        }

        return remote;
    }

    /// <summary>
    /// Body of the token request endpoint.
    /// </summary>
    public class TokenRequest
    {
        [JsonPropertyName("Puid")]
        public required string ProductUserId { get; init; }

        [JsonPropertyName("Username")]
        public required string Username { get; init; }

        [JsonPropertyName("ClientVersion")]
        public required int ClientVersion { get; init; }

        [JsonPropertyName("Language")]
        public required Language Language { get; init; }

        /// <summary>
        /// Gets the friend code, when the client sends one. Optional and nullable on purpose:
        /// marking it required would make the request fail for every client that omits it.
        /// </summary>
        [JsonPropertyName("FriendCode")]
        public string? FriendCode { get; init; }

        /// <summary>
        /// Gets any properties the client sent that are not modelled above. Captured so the
        /// payload can be inspected without guessing; see <see cref="ReportUnknownFields" />.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraProperties { get; init; }
    }

    /// <summary>
    /// Token that is returned to the user with a "signature".
    /// </summary>
    public sealed class Token
    {
        [JsonPropertyName("Content")]
        public required TokenPayload Content { get; init; }

        [JsonPropertyName("Hash")]
        public required string Hash { get; init; }
    }

    /// <summary>
    /// Actual token contents.
    /// </summary>
    public sealed class TokenPayload
    {
        private static readonly DateTime DefaultExpiryDate = new(2012, 12, 21);

        [JsonPropertyName("Puid")]
        public required string ProductUserId { get; init; }

        [JsonPropertyName("ClientVersion")]
        public required int ClientVersion { get; init; }

        [JsonPropertyName("ExpiresAt")]
        public DateTime ExpiresAt { get; init; } = DefaultExpiryDate;
    }
}
