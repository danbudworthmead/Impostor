namespace Impostor.Server.Net.Manager
{
    /// <summary>
    ///     Client-claimed identity, for display to other players only.
    /// </summary>
    /// <param name="ProductUserId">The Epic Online Services product user id the client claimed.</param>
    /// <param name="FriendCode">The friend code the client claimed.</param>
    public readonly record struct ClientIdentity(string ProductUserId, string FriendCode);
}
