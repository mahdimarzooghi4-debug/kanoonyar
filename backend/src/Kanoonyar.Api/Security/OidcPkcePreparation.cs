using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Kanoonyar.Api.Security;

/// <summary>
/// An isolated, in-memory PKCE + browser-binding preparation contract.
/// NOT registered in dependency injection and has NO HTTP route, login,
/// callback handler, token exchange, session cookie, user grant or persistence.
/// The final BFF/session model remains blocked by B02/T04 approval.
/// </summary>
public sealed class OidcPkcePreparation
{
    private sealed record Pending(
        string Issuer, string ClientId, string RedirectUri, string Binding,
        string Nonce, string Verifier, DateTimeOffset ExpiresAt);

    public sealed record Initiation(
        string AuthorizationUrl, string State, string BrowserBinding);
    public sealed record Redemption(string CodeVerifier, string Nonce, string RedirectUri);

    private readonly ConcurrentDictionary<string, Pending> _pending =
        new(StringComparer.Ordinal);

    private static string RandomBase64Url()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64Url(bytes);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void RequireHttpsEndpoint(string? url, bool allowQuery)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Fragment) ||
            (!allowQuery && !string.IsNullOrEmpty(parsed.Query)) ||
            string.IsNullOrWhiteSpace(parsed.Host))
            throw new ArgumentException("An explicitly pinned HTTPS OIDC URL is required.");
    }

    /// <summary>
    /// All trust inputs MUST be pinned by future approved server configuration,
    /// never from HTTP user parameters or token claims. No implicit TTL:
    /// expiresAt must be explicitly supplied by a future approved session policy.
    /// No provider is contacted and no operation is activated.
    /// </summary>
    public Initiation Prepare(
        string issuer, string authorizationEndpoint, string clientId,
        string redirectUri, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        RequireHttpsEndpoint(issuer, allowQuery: false);
        RequireHttpsEndpoint(authorizationEndpoint, allowQuery: false);
        RequireHttpsEndpoint(redirectUri, allowQuery: false);
        if (string.IsNullOrWhiteSpace(clientId) || clientId.Length > 256 ||
            !string.Equals(clientId, clientId.Trim(), StringComparison.Ordinal) ||
            expiresAt <= now)
            throw new ArgumentException("Invalid explicit OIDC client or expiry configuration.");

        var state = RandomBase64Url();
        var nonce = RandomBase64Url();
        var verifier = RandomBase64Url();
        var binding = RandomBase64Url();
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Base64Url(digest);
        // The pinned issuer is NOT inferred from the authorization URL,
        // and must separately match trusted metadata in a future BFF.
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = "openid",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["nonce"] = nonce
        };
        var encoded = string.Join("&", query.Select(item =>
            Uri.EscapeDataString(item.Key) + "=" + Uri.EscapeDataString(item.Value)));
        var pending = new Pending(issuer, clientId, redirectUri, binding, nonce, verifier, expiresAt);
        if (!_pending.TryAdd(state, pending))
            throw new InvalidOperationException("A generated PKCE state collided; request rejected.");

        return new Initiation(authorizationEndpoint + "?" + encoded, state, binding);
    }

    /// <summary>
    /// Match the exact original state, pinned issuer/client/redirect and a
    /// server-issued browser-binding secret before atomically consuming state.
    /// The callback code cannot be exchanged here; the future BFF must still
    /// implement issuer/signature/nonce validation, secure session lifecycle,
    /// trusted callback origin, CSRF policy and audited logout/revocation.
    /// </summary>
    public bool TryConsume(
        string? state, string? browserBinding, string? issuer, string? clientId,
        string? redirectUri, string? authorizationCode,
        DateTimeOffset now, out Redemption? redemption)
    {
        redemption = null;
        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(browserBinding) ||
            string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(redirectUri) || string.IsNullOrWhiteSpace(authorizationCode) ||
            !_pending.TryGetValue(state, out var pending) || now >= pending.ExpiresAt ||
            !string.Equals(issuer, pending.Issuer, StringComparison.Ordinal) ||
            !string.Equals(clientId, pending.ClientId, StringComparison.Ordinal) ||
            !string.Equals(redirectUri, pending.RedirectUri, StringComparison.Ordinal))
            return false;

        var supplied = Encoding.UTF8.GetBytes(browserBinding);
        var actual = Encoding.UTF8.GetBytes(pending.Binding);
        if (supplied.Length != actual.Length ||
            !CryptographicOperations.FixedTimeEquals(supplied, actual))
            return false;
        if (!_pending.TryRemove(state, out var removed) || !ReferenceEquals(removed, pending))
            return false;

        redemption = new Redemption(pending.Verifier, pending.Nonce, pending.RedirectUri);
        return true;
    }

    // The store is strictly process-local and is not a distributed/session store.
    // A future supported BFF must explicitly decide persistence, expiry, logout
    // and revocation before enabling any authentication route.
}
