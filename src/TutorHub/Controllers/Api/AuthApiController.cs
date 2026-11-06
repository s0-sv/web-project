using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TutorHub.Contracts;
using TutorHub.Models;
using TutorHub.Services;

namespace TutorHub.Controllers.Api;

[Route("api/auth")]
public class AuthApiController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    JwtTokenService tokens) : ApiControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<TokenResponse>> Register(RegisterRequest request)
    {
        var user = new ApplicationUser { UserName = request.Email, Email = request.Email, FullName = request.FullName };
        var result = await users.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            return IdentityFailure(result);
        }

        return StatusCode(StatusCodes.Status201Created, await tokens.CreateAsync(user));
    }

    [AllowAnonymous]
    [HttpPost("token")]
    public async Task<ActionResult<TokenResponse>> Token(TokenRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return InvalidCredentials();
        }

        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        return result.Succeeded ? await tokens.CreateAsync(user) : InvalidCredentials();
    }

    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserDto>> Me()
    {
        var user = await users.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return user is null ? Unauthorized() : new CurrentUserDto(user.Id, user.Email!, user.FullName);
    }

    private ObjectResult InvalidCredentials() =>
        Problem(
            detail: "The email or password is incorrect, or the account is temporarily locked.",
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Invalid credentials");

    private ActionResult IdentityFailure(IdentityResult result)
    {
        if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            return ConflictProblem("An account with this email already exists.");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.Code, error.Description);
        }

        return ValidationProblem(ModelState);
    }
}
