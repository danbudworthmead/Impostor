using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Impostor.Api.Utils;

namespace Impostor.Server.Net.Manager
{
    /// <summary>
    ///     Bridges the HTTP matchmaker token request and the UDP connection that follows it.
    ///
    ///     Among Us asks <c>POST /api/user</c> for a token seconds before it opens its Hazel
    ///     connection, and that HTTP request is the only place the client tells us its product
    ///     user id and friend code. Nothing in the UDP handshake links the two, so entries are
    ///     stored per source address and claimed again when the connection arrives.
    /// </summary>
    /// <remarks>
    ///     Everything stored here is client-supplied and unverified. It exists so other players
    ///     can see a friend code, and must never be used for authorization or moderation.
    /// </remarks>
    public sealed class ClientIdentityCache
    {
        /// <summary>
        ///     Entries kept per source address. A household or cafe behind one NAT can hold
        ///     several players; beyond this the oldest is evicted.
        /// </summary>
        private const int MaxEntriesPerAddress = 16;

        /// <summary>
        ///     Total addresses tracked, to bound memory if the endpoint is ever hammered.
        /// </summary>
        private const int MaxAddresses = 4096;

        /// <summary>
        ///     A full sweep runs every time this many entries have been stored, which avoids
        ///     needing a background timer and its lifetime management.
        /// </summary>
        private const int SweepInterval = 256;

        /// <summary>
        ///     How long an unclaimed entry survives. The gap between the token request and the
        ///     handshake is seconds; this is generous to absorb a slow client or a retry.
        /// </summary>
        private static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(2);

        private readonly ConcurrentDictionary<IPAddress, Bucket> _buckets = new();
        private readonly IDateTimeProvider _dateTimeProvider;
        private int _storesSinceSweep;

        public ClientIdentityCache(IDateTimeProvider dateTimeProvider)
        {
            _dateTimeProvider = dateTimeProvider;
        }

        /// <summary>
        ///     Records what a client claimed about itself when it asked for a token.
        /// </summary>
        /// <param name="address">Address the token request came from.</param>
        /// <param name="username">Name the client gave the matchmaker.</param>
        /// <param name="productUserId">Product user id the client claimed, or an empty string.</param>
        /// <param name="friendCode">Friend code the client claimed, or an empty string.</param>
        public void Store(IPAddress address, string username, string productUserId, string friendCode)
        {
            if (Interlocked.Increment(ref _storesSinceSweep) >= SweepInterval)
            {
                Interlocked.Exchange(ref _storesSinceSweep, 0);
                SweepExpired();
            }

            if (!_buckets.TryGetValue(address, out var bucket))
            {
                if (_buckets.Count >= MaxAddresses)
                {
                    // Already swept above; drop rather than grow without bound. A miss here only
                    // costs an empty friend code, so failing open is the right trade.
                    return;
                }

                bucket = _buckets.GetOrAdd(address, _ => new Bucket());
            }

            bucket.Store(new Entry(username, productUserId, friendCode, _dateTimeProvider.UtcNow), EntryLifetime);
        }

        /// <summary>
        ///     Looks up what the connecting client claimed earlier over HTTP.
        /// </summary>
        /// <param name="address">Address the game connection came from.</param>
        /// <param name="name">Name from the connection handshake.</param>
        /// <param name="identity">The identity that was claimed, when one was found.</param>
        /// <returns><see langword="true" /> when a single unambiguous entry was found.</returns>
        public bool TryClaim(IPAddress address, string name, out ClientIdentity identity)
        {
            identity = default;

            if (!_buckets.TryGetValue(address, out var bucket))
            {
                return false;
            }

            if (!bucket.TryClaim(name, _dateTimeProvider.UtcNow, EntryLifetime, out var entry))
            {
                return false;
            }

            identity = new ClientIdentity(entry.ProductUserId, entry.FriendCode);
            return true;
        }

        private void SweepExpired()
        {
            var now = _dateTimeProvider.UtcNow;

            foreach (var pair in _buckets)
            {
                if (pair.Value.PruneExpired(now, EntryLifetime))
                {
                    _buckets.TryRemove(pair.Key, out _);
                }
            }
        }

        private readonly record struct Entry(string Username, string ProductUserId, string FriendCode, DateTimeOffset LastSeen)
        {
            public bool Matches(Entry other)
            {
                return string.Equals(Username, other.Username, StringComparison.OrdinalIgnoreCase)
                       && string.Equals(ProductUserId, other.ProductUserId, StringComparison.Ordinal);
            }
        }

        private sealed class Bucket
        {
            private readonly List<Entry> _entries = new();

            public void Store(Entry entry, TimeSpan lifetime)
            {
                lock (_entries)
                {
                    PruneExpiredCore(entry.LastSeen, lifetime);

                    // Clients re-request tokens on retries and lobby refreshes. Replacing the
                    // matching entry rather than appending keeps a single player from growing
                    // the bucket until they evict their own housemates.
                    for (var i = 0; i < _entries.Count; i++)
                    {
                        if (_entries[i].Matches(entry))
                        {
                            _entries[i] = entry;
                            return;
                        }
                    }

                    if (_entries.Count >= MaxEntriesPerAddress)
                    {
                        _entries.RemoveAt(0);
                    }

                    _entries.Add(entry);
                }
            }

            public bool TryClaim(string name, DateTimeOffset now, TimeSpan lifetime, out Entry claimed)
            {
                lock (_entries)
                {
                    PruneExpiredCore(now, lifetime);

                    var index = -1;

                    for (var i = 0; i < _entries.Count; i++)
                    {
                        if (string.Equals(_entries[i].Username, name, StringComparison.OrdinalIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }

                    // Fall back to the only candidate on this address. With more than one and no
                    // name match we refuse: showing player A's friend code to player B would be
                    // worse than showing none at all.
                    if (index == -1)
                    {
                        if (_entries.Count != 1)
                        {
                            claimed = default;
                            return false;
                        }

                        index = 0;
                    }

                    // Slide the expiry so a player who finishes a game and reconnects without a
                    // fresh token request still resolves.
                    claimed = _entries[index] with { LastSeen = now };
                    _entries[index] = claimed;
                    return true;
                }
            }

            public bool PruneExpired(DateTimeOffset now, TimeSpan lifetime)
            {
                lock (_entries)
                {
                    PruneExpiredCore(now, lifetime);
                    return _entries.Count == 0;
                }
            }

            private void PruneExpiredCore(DateTimeOffset now, TimeSpan lifetime)
            {
                for (var i = _entries.Count - 1; i >= 0; i--)
                {
                    if (now - _entries[i].LastSeen > lifetime)
                    {
                        _entries.RemoveAt(i);
                    }
                }
            }
        }
    }
}
