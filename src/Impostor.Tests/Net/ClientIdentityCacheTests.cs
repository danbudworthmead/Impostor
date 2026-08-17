using System;
using System.Net;
using Impostor.Api.Utils;
using Impostor.Server.Net.Manager;
using Xunit;

namespace Impostor.Tests.Net
{
    public class ClientIdentityCacheTests
    {
        private static readonly IPAddress Address = IPAddress.Parse("203.0.113.10");
        private static readonly IPAddress OtherAddress = IPAddress.Parse("203.0.113.11");

        [Fact]
        public void Claim_ReturnsWhatWasStored()
        {
            var clock = new FakeClock();
            var cache = new ClientIdentityCache(clock);

            cache.Store(Address, "Red", "puid-red", "apple#0001");

            Assert.True(cache.TryClaim(Address, "Red", out var identity));
            Assert.Equal("puid-red", identity.ProductUserId);
            Assert.Equal("apple#0001", identity.FriendCode);
        }

        [Fact]
        public void Claim_MatchesNameCaseInsensitively()
        {
            var clock = new FakeClock();
            var cache = new ClientIdentityCache(clock);

            cache.Store(Address, "Red", "puid-red", "apple#0001");

            Assert.True(cache.TryClaim(Address, "rED", out var identity));
            Assert.Equal("puid-red", identity.ProductUserId);
        }

        [Fact]
        public void Claim_UnknownAddress_Fails()
        {
            var cache = new ClientIdentityCache(new FakeClock());

            cache.Store(Address, "Red", "puid-red", "apple#0001");

            Assert.False(cache.TryClaim(OtherAddress, "Red", out _));
        }

        [Fact]
        public void Claim_SharedAddress_DisambiguatesByName()
        {
            var cache = new ClientIdentityCache(new FakeClock());

            cache.Store(Address, "Red", "puid-red", "apple#0001");
            cache.Store(Address, "Blue", "puid-blue", "pear#0002");

            Assert.True(cache.TryClaim(Address, "Blue", out var identity));
            Assert.Equal("puid-blue", identity.ProductUserId);
        }

        [Fact]
        public void Claim_SharedAddressWithNoNameMatch_RefusesRatherThanGuessing()
        {
            var cache = new ClientIdentityCache(new FakeClock());

            cache.Store(Address, "Red", "puid-red", "apple#0001");
            cache.Store(Address, "Blue", "puid-blue", "pear#0002");

            // Handing one player's friend code to another would be worse than showing none.
            Assert.False(cache.TryClaim(Address, "Green", out _));
        }

        [Fact]
        public void Claim_SoleEntryOnAddress_MatchesEvenWhenNameDiffers()
        {
            var cache = new ClientIdentityCache(new FakeClock());

            cache.Store(Address, "Red", "puid-red", "apple#0001");

            // The name sent to the matchmaker can differ from the one in the handshake.
            Assert.True(cache.TryClaim(Address, "Renamed", out var identity));
            Assert.Equal("puid-red", identity.ProductUserId);
        }

        [Fact]
        public void Claim_AfterLifetime_Fails()
        {
            var clock = new FakeClock();
            var cache = new ClientIdentityCache(clock);

            cache.Store(Address, "Red", "puid-red", "apple#0001");
            clock.Advance(TimeSpan.FromMinutes(3));

            Assert.False(cache.TryClaim(Address, "Red", out _));
        }

        [Fact]
        public void Claim_SlidesExpirySoReconnectsStillResolve()
        {
            var clock = new FakeClock();
            var cache = new ClientIdentityCache(clock);

            cache.Store(Address, "Red", "puid-red", "apple#0001");

            // A player finishing a game and rejoining does not request a new token.
            clock.Advance(TimeSpan.FromSeconds(90));
            Assert.True(cache.TryClaim(Address, "Red", out _));

            clock.Advance(TimeSpan.FromSeconds(90));
            Assert.True(cache.TryClaim(Address, "Red", out _));
        }

        [Fact]
        public void Store_RepeatedForSamePlayer_DoesNotEvictOthers()
        {
            var cache = new ClientIdentityCache(new FakeClock());

            cache.Store(Address, "Blue", "puid-blue", "pear#0002");

            // Clients re-request tokens on retries and lobby refreshes. Without replacement this
            // would push Blue out of the bucket.
            for (var i = 0; i < 50; i++)
            {
                cache.Store(Address, "Red", "puid-red", "apple#0001");
            }

            Assert.True(cache.TryClaim(Address, "Blue", out var identity));
            Assert.Equal("puid-blue", identity.ProductUserId);
        }

        [Fact]
        public void Store_LatestValueWins()
        {
            var cache = new ClientIdentityCache(new FakeClock());

            cache.Store(Address, "Red", "puid-red", "apple#0001");
            cache.Store(Address, "Red", "puid-red", "cherry#0009");

            Assert.True(cache.TryClaim(Address, "Red", out var identity));
            Assert.Equal("cherry#0009", identity.FriendCode);
        }

        [Fact]
        public void Store_BeyondPerAddressCap_EvictsOldest()
        {
            var cache = new ClientIdentityCache(new FakeClock());

            cache.Store(Address, "First", "puid-first", "first#0001");

            for (var i = 0; i < 16; i++)
            {
                cache.Store(Address, $"Player{i}", $"puid-{i}", $"code#{i:0000}");
            }

            Assert.False(cache.TryClaim(Address, "First", out _));
            Assert.True(cache.TryClaim(Address, "Player15", out _));
        }

        private sealed class FakeClock : IDateTimeProvider
        {
            public DateTimeOffset UtcNow { get; private set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

            public void Advance(TimeSpan amount)
            {
                UtcNow += amount;
            }
        }
    }
}
