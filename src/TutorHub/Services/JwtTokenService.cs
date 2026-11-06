using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TutorHub.Contracts;
using TutorHub.Models;

namespace TutorHub.Services;

public class JwtTokenService(
    IConfiguration config,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory)
{
    public const string Issuer = "TutorHub";
    public const string Audience = "TutorHub";
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public static SymmetricSecurityKey SigningKey(IConfiguration config)
    {
        var key = config["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is not set. Use user-secrets or the Jwt__SigningKey variable.");
        var bytes = Encoding.UTF8.GetBytes(key);

        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes long.");
        }

        return new SymmetricSecurityKey(bytes);
    }

    public async Task<TokenResponse> CreateAsync(ApplicationUser user)
    {
        var principal = await claimsFactory.CreateAsync(user);
        var claims = principal.Claims.Where(c => c.Type != "AspNet.Identity.SecurityStamp").ToList();
        claims.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.Add(Lifetime),
            SigningCredentials = new SigningCredentials(SigningKey(config), SecurityAlgorithms.HmacSha256)
        });

        return new TokenResponse(token, "Bearer", (int)Lifetime.TotalSeconds);
    }
}
