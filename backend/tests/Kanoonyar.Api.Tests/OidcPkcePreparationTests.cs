using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Kanoonyar.Api.Security;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class OidcPkcePreparationTests
{
    private const string Issuer = "https://id.example.invalid/realms/kanoonyar";
    private const string AuthorizationEndpoint =
        "https://id.example.invalid/realms/kanoonyar/protocol/openid-connect/auth";
    private const string ClientId = "audience-bff-explicit-config";
    private const string Callback = "https://audience.example.invalid/oidc/callback";
    private static readonly DateTimeOffset Now = new(2026,10,14,12,0,0,TimeSpan.Zero);

    private static OidcPkcePreparation.Initiation Prepare(OidcPkcePreparation guard) =>
        guard.Prepare(Issuer,AuthorizationEndpoint,ClientId,Callback,Now,Now.AddMinutes(4));

    private static Dictionary<string,string> Query(string url) =>
        new Uri(url).Query.TrimStart('?').Split('&')
            .Select(entry=>entry.Split('=',2))
            .ToDictionary(parts=>Uri.UnescapeDataString(parts[0]), parts=>Uri.UnescapeDataString(parts[1]));

    private static bool Consume(OidcPkcePreparation guard,OidcPkcePreparation.Initiation t,
        out OidcPkcePreparation.Redemption? proof, string? state=null,string? binding=null,
        string? issuer=null,string? client=null,string? redirect=null,string? code=null,
        DateTimeOffset? now=null) =>
        guard.TryConsume(state ?? t.State,binding ?? t.BrowserBinding,
            issuer ?? Issuer,client ?? ClientId,redirect ?? Callback,
            code ?? "synthetic-non-empty-authorization-code",
            now ?? Now, out proof);

    [Fact]
    public void Mandatory_oidc_parameters_have_random_state_nonce_and_sha256_pkce_challenge_only()
    {
        var guard = new OidcPkcePreparation();
        var start = Prepare(guard);
        var parameters = Query(start.AuthorizationUrl);
        Assert.StartsWith(AuthorizationEndpoint+"?", start.AuthorizationUrl, StringComparison.Ordinal);
        Assert.Equal("code",parameters["response_type"]);
        Assert.Equal("openid",parameters["scope"]);
        Assert.Equal("S256",parameters["code_challenge_method"]);
        Assert.Equal(ClientId,parameters["client_id"]);
        Assert.Equal(Callback,parameters["redirect_uri"]);
        Assert.Equal(start.State,parameters["state"]);
        Assert.True(start.State.Length>=43);
        Assert.True(start.BrowserBinding.Length>=43);
        Assert.True(parameters["nonce"].Length>=43);
        Assert.True(parameters["code_challenge"].Length>=43);
        Assert.NotEqual(start.State,start.BrowserBinding);
        Assert.NotEqual(parameters["nonce"],start.State);
        Assert.DoesNotContain("code_verifier",start.AuthorizationUrl,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(start.BrowserBinding,start.AuthorizationUrl,StringComparison.Ordinal);
        Assert.True(Consume(guard,start,out var secret));
        Assert.NotNull(secret);
        var bytes=SHA256.HashData(Encoding.ASCII.GetBytes(secret.CodeVerifier));
        var expected=Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
        Assert.Equal(expected,parameters["code_challenge"]);
        Assert.Equal(secret.Nonce,parameters["nonce"]);
        Assert.Equal(Callback,secret.RedirectUri);
        Assert.False(Consume(guard,start,out _));
    }

    [Fact]
    public void Wrong_actor_binding_state_issuer_client_redirect_and_missing_code_fail_closed()
    {
        var guard = new OidcPkcePreparation();
        var item = Prepare(guard);
        Assert.False(Consume(guard,item,out _,state:Prepare(guard).State,binding:item.BrowserBinding));
        Assert.False(Consume(guard,item,out _,binding:"foreign-browser-binding"));
        Assert.False(Consume(guard,item,out _,issuer:"https://evil.invalid/realms/other"));
        Assert.False(Consume(guard,item,out _,client:"another-client"));
        Assert.False(Consume(guard,item,out _,redirect:"https://evil.invalid/callback"));
        Assert.False(Consume(guard,item,out _,code:" "));
        Assert.True(Consume(guard,item,out _));
        Assert.False(Consume(guard,item,out _));
    }

    [Fact]
    public void Explicit_expiry_not_a_default_and_replay_is_rejected_even_under_concurrency()
    {
        var guard = new OidcPkcePreparation();
        var ticket=Prepare(guard);
        Assert.False(Consume(guard,ticket,out _,now:Now.AddMinutes(4)));
        Assert.False(Consume(guard,ticket,out _,now:Now.AddDays(1)));

        var usable=Prepare(guard);
        var results=new ConcurrentBag<bool>();
        Parallel.For(0,64,index=>results.Add(Consume(guard,usable,out _)));
        Assert.Equal(1,results.Count(x=>x));
        Assert.Equal(63,results.Count(x=>!x));
        Assert.False(Consume(guard,usable,out _));
    }

    [Fact]
    public void Requests_and_callbacks_require_explicit_https_pinned_configuration()
    {
        var guard=new OidcPkcePreparation();
        foreach(var bad in new[]{ "", "http://issuer.invalid","https://u:p@issuer.invalid",
            "https://issuer.invalid/?redirect=https://evil.invalid",
            "https://issuer.invalid/#evil", "/relative" })
        {
            Assert.Throws<ArgumentException>(()=>guard.Prepare(
                bad,AuthorizationEndpoint,ClientId,Callback,Now,Now.AddMinutes(4)));
            Assert.Throws<ArgumentException>(()=>guard.Prepare(
                Issuer,bad,ClientId,Callback,Now,Now.AddMinutes(4)));
            Assert.Throws<ArgumentException>(()=>guard.Prepare(
                Issuer,AuthorizationEndpoint,ClientId,bad,Now,Now.AddMinutes(4)));
        }
        Assert.Throws<ArgumentException>(()=>guard.Prepare(
            Issuer,AuthorizationEndpoint,"  ",Callback,Now,Now.AddMinutes(4)));
        Assert.Throws<ArgumentException>(()=>guard.Prepare(
            Issuer,AuthorizationEndpoint,ClientId,Callback,Now,Now));
        Assert.Throws<ArgumentException>(()=>guard.Prepare(
            Issuer,AuthorizationEndpoint,ClientId,Callback,Now,Now.AddMinutes(-1)));
    }

    [Fact]
    public void Multiple_synthetic_eighty_center_subjects_cannot_reuse_others_browser_binding()
    {
        var guard=new OidcPkcePreparation();
        var tickets=Enumerable.Range(0,80).Select(_=>Prepare(guard)).ToArray();
        Assert.Equal(80,tickets.Select(t=>t.State).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(80,tickets.Select(t=>t.BrowserBinding).Distinct(StringComparer.Ordinal).Count());
        foreach(var i in Enumerable.Range(0,80))
        {
            Assert.False(Consume(guard,tickets[i],out _,
                binding:tickets[(i+1)%80].BrowserBinding));
            Assert.True(Consume(guard,tickets[i],out _));
        }
    }
}
