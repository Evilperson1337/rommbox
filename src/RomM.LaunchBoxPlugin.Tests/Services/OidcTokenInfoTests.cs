using System;
using FluentAssertions;
using RomMbox.Services.Auth;

namespace RomMbox.Tests.Services
{
    public sealed class OidcTokenInfoTests
    {
        [Fact]
        public void IsAccessTokenExpired_ShouldReturnTrue_WhenExpiryFallsInsideBuffer()
        {
            var token = new OidcTokenInfo();
            token.SetExpiresAtUtc(DateTimeOffset.UtcNow.AddSeconds(10));

            token.IsAccessTokenExpired(TimeSpan.FromSeconds(30)).Should().BeTrue();
        }

        [Fact]
        public void FromTokenResponse_ShouldPopulateExpiryAndType()
        {
            var token = OidcTokenInfo.FromTokenResponse(new TokenResponse
            {
                AccessToken = "access",
                RefreshToken = "refresh",
                TokenType = "Bearer",
                Expires = 120
            });

            token.AccessToken.Should().Be("access");
            token.RefreshToken.Should().Be("refresh");
            token.TokenType.Should().Be("Bearer");
            token.GetExpiresAtUtc().Should().NotBeNull();
        }
    }
}
