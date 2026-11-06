using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TutorHub.Services;
using Xunit;

namespace TutorHub.AuthTests;

[Collection(AuthCollection.Name)]
public class ApiAuthTests(AuthTestFactory factory)
{
    private static string Sign(string key, string issuer = JwtTokenService.Issuer, string audience = JwtTokenService.Audience, DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        var expiry = expires ?? now.AddHours(1);

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = expiry.AddHours(-2),
            IssuedAt = expiry.AddHours(-2),
            Expires = expiry,
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "someone"),
                new Claim(TenantContext.TenantClaim, "1"),
                new Claim(ClaimTypes.Role, "Owner")
            ]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256)
        });
    }

    [Theory]
    [InlineData("/api/subjects")]
    [InlineData("/api/subjects/1")]
    [InlineData("/api/tutors")]
    [InlineData("/api/students")]
    [InlineData("/api/lessons")]
    [InlineData("/api/lessons/1/materials")]
    [InlineData("/api/materials")]
    [InlineData("/api/materials/1/file")]
    [InlineData("/api/analytics")]
    [InlineData("/api/auth/me")]
    public async Task Anonymous_read_is_rejected(string path)
    {
        var response = await factory.Anonymous().GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_write_is_rejected()
    {
        var response = await factory.Anonymous().PostAsJsonAsync("/api/subjects", new { name = "Nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Registered_token_grants_access_and_identifies_the_user()
    {
        var user = await factory.RegisterAsync();

        var me = await (await user.Client.GetAsync("/api/auth/me")).ReadJsonAsync();

        Assert.Equal(user.Email, me.GetProperty("email").GetString());
        Assert.Equal("Owner", me.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Token_endpoint_issues_a_working_token()
    {
        var user = await factory.RegisterAsync();

        var response = await factory.Anonymous().PostAsJsonAsync("/api/auth/token", new { email = user.Email, password = user.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal("Bearer", body.GetProperty("tokenType").GetString());
        Assert.Equal(3600, body.GetProperty("expiresIn").GetInt32());

        var client = factory.WithToken(body.GetProperty("accessToken").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/subjects")).StatusCode);
    }

    [Fact]
    public async Task Token_holds_tenant_and_role_but_no_security_stamp()
    {
        var user = await factory.RegisterAsync();

        var token = new JsonWebToken(user.Token);

        Assert.Contains(token.Claims, c => c.Type == TenantContext.TenantClaim);
        Assert.Contains(token.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Owner");
        Assert.DoesNotContain(token.Claims, c => c.Type.Contains("SecurityStamp"));
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_look_the_same()
    {
        var user = await factory.RegisterAsync();

        var wrong = await factory.Anonymous().PostAsJsonAsync("/api/auth/token", new { email = user.Email, password = "Wrong-password-1" });
        var unknown = await factory.Anonymous().PostAsJsonAsync("/api/auth/token", new { email = "nobody@example.com", password = "Wrong-password-1" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(
            (await wrong.ReadJsonAsync()).GetProperty("detail").GetString(),
            (await unknown.ReadJsonAsync()).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Account_locks_after_five_failures_even_for_the_right_password()
    {
        var user = await factory.RegisterAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await factory.Anonymous().PostAsJsonAsync("/api/auth/token", new { email = user.Email, password = "Wrong-password-1" });
        }

        var response = await factory.Anonymous().PostAsJsonAsync("/api/auth/token", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_regardless_of_case()
    {
        var user = await factory.RegisterAsync();

        var response = await factory.Anonymous().PostAsJsonAsync("/api/auth/register", new
        {
            email = user.Email.ToUpperInvariant(),
            password = "Test-password-1",
            fullName = "Copy",
            workspaceName = "Copy"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("short1")]
    [InlineData("no-digits-here")]
    public async Task Weak_password_is_rejected(string password)
    {
        var response = await factory.Anonymous().PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{AuthTestFactory.Unique("weak")}@example.com",
            password,
            fullName = "Weak",
            workspaceName = "Weak"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Registration_needs_a_workspace_or_an_invite()
    {
        var response = await factory.Anonymous().PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{AuthTestFactory.Unique("none")}@example.com",
            password = "Test-password-1",
            fullName = "None"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_token_is_rejected()
    {
        var response = await factory.WithToken("not-a-token").GetAsync("/api/subjects");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Control_token_signed_with_the_real_key_is_accepted()
    {
        var token = Sign(AuthTestFactory.SigningKeyValue);
        var response = await factory.WithToken(token).GetAsync("/api/subjects");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        var token = Sign("another-signing-key-that-is-long-enough-for-hs256");
        var response = await factory.WithToken(token).GetAsync("/api/subjects");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var token = Sign(AuthTestFactory.SigningKeyValue, expires: DateTime.UtcNow.AddHours(-1));
        var response = await factory.WithToken(token).GetAsync("/api/subjects");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_for_another_issuer_is_rejected()
    {
        var token = Sign(AuthTestFactory.SigningKeyValue, issuer: "SomeoneElse");
        var response = await factory.WithToken(token).GetAsync("/api/subjects");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_for_another_audience_is_rejected()
    {
        var token = Sign(AuthTestFactory.SigningKeyValue, audience: "SomeoneElse");
        var response = await factory.WithToken(token).GetAsync("/api/subjects");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unsigned_token_is_rejected()
    {
        static string Encode(string json) => Base64UrlEncoder.Encode(json);

        var header = Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = Encode($"{{\"iss\":\"{JwtTokenService.Issuer}\",\"aud\":\"{JwtTokenService.Audience}\",\"tenant_id\":\"1\",\"exp\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}");

        var response = await factory.WithToken($"{header}.{payload}.").GetAsync("/api/subjects");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
