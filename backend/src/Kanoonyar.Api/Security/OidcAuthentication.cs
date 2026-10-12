using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Kanoonyar.Api.Security;

public static class OidcAuthentication
{
    public static bool AddConfiguredOidc(this WebApplicationBuilder builder)
    {
        var authority = builder.Configuration["OIDC_AUTHORITY"];
        var audience = builder.Configuration["OIDC_AUDIENCE"];
        var hasAuthority = !string.IsNullOrWhiteSpace(authority);
        var hasAudience = !string.IsNullOrWhiteSpace(audience);
        if (hasAuthority != hasAudience)
            throw new InvalidOperationException("OIDC_AUTHORITY and OIDC_AUDIENCE must be configured together.");
        if (!hasAuthority)
            return false;

        if (!Uri.TryCreate(authority, UriKind.Absolute, out var issuer) ||
            issuer.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(issuer.UserInfo) ||
            !string.IsNullOrEmpty(issuer.Query) || !string.IsNullOrEmpty(issuer.Fragment))
        {
            throw new InvalidOperationException("OIDC_AUTHORITY must be an HTTPS issuer URL without credentials, query, or fragment.");
        }

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Preserve the standard OIDC subject name verbatim. Inbound claims
                // remapping can silently turn "sub" into another claim type and
                // break the authenticated actor boundary used by center handlers.
                options.MapInboundClaims = false;
                options.Authority = authority;
                options.Audience = audience;
                options.RequireHttpsMetadata = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });
        builder.Services.AddAuthorization();
        return true;
    }
}
