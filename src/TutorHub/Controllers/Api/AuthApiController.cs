using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TutorHub.Contracts;
using TutorHub.Data;
using TutorHub.Models;
using TutorHub.Services;

namespace TutorHub.Controllers.Api;

[Route("api/auth")]
public class AuthApiController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    JwtTokenService tokens,
    TenantRegistration registration,
    AppDbContext db,
    TenantContext tenant) : ApiControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<TokenResponse>> Register(RegisterRequest request)
    {
        var result = await registration.RegisterAsync(new RegistrationRequest(
            request.Email, request.FullName, request.Password, request.WorkspaceName, request.InviteCode));

        return result.User is null
            ? IdentityFailure(result.Errors)
            : StatusCode(StatusCodes.Status201Created, await tokens.CreateAsync(result.User));
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
        if (user is null)
        {
            return Unauthorized();
        }

        var current = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == user.TenantId);
        return new CurrentUserDto(
            user.Id,
            user.Email!,
            user.FullName,
            user.TenantId,
            current.Name,
            user.Role,
            user.StudentId,
            current.Plan,
            FeatureFlags.FeaturesFor(current.Plan));
    }

    [Authorize(Policy = AppPolicies.OwnerOnly)]
    [HttpPost("invite")]
    public async Task<ActionResult<InviteCodeDto>> RegenerateMemberInvite()
    {
        var current = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId);
        current.MemberInviteCode = Tenant.NewInviteCode();
        await db.SaveChangesAsync();
        return new InviteCodeDto(current.MemberInviteCode);
    }

    [Authorize(Policy = AppPolicies.OwnerOnly)]
    [HttpPut("plan")]
    public async Task<IActionResult> ChangePlan(PlanChangeRequest request)
    {
        var current = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId);
        current.Plan = request.Plan;
        await db.SaveChangesAsync();
        return NoContent();
    }

    private ObjectResult InvalidCredentials() =>
        Problem(
            detail: "The email or password is incorrect, or the account is temporarily locked.",
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Invalid credentials");

    private ActionResult IdentityFailure(IReadOnlyList<IdentityError> errors)
    {
        if (errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            return ConflictProblem("An account with this email already exists.");
        }

        foreach (var error in errors)
        {
            ModelState.AddModelError(error.Code, error.Description);
        }

        return ValidationProblem(ModelState);
    }
}
