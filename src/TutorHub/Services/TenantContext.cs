using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TutorHub.Data;
using TutorHub.Models;

namespace TutorHub.Services;

public class TenantContext(IHttpContextAccessor accessor)
{
    public const string TenantClaim = "tenant_id";
    public const string StudentClaim = "student_id";

    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public int? TenantId => ReadInt(TenantClaim);

    public int? StudentId => ReadInt(StudentClaim);

    public bool IsStudent => User?.IsInRole(TenantRoles.Student) == true;

    private int? ReadInt(string claim) =>
        int.TryParse(User?.FindFirstValue(claim), out var value) ? value : null;
}

public class AppUserClaimsFactory(UserManager<ApplicationUser> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(TenantContext.TenantClaim, user.TenantId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role));

        if (user.StudentId is { } studentId)
        {
            identity.AddClaim(new Claim(TenantContext.StudentClaim, studentId.ToString()));
        }

        return identity;
    }
}

public record RegistrationRequest(
    string Email,
    string FullName,
    string? Password,
    string? WorkspaceName,
    string? InviteCode,
    UserLoginInfo? ExternalLogin = null);

public record RegistrationResult(ApplicationUser? User, IReadOnlyList<IdentityError> Errors)
{
    public static RegistrationResult Failure(string code, string description) =>
        new(null, [new IdentityError { Code = code, Description = description }]);
}

public class TenantRegistration(AppDbContext db, UserManager<ApplicationUser> users)
{
    public const string InvalidInviteCode = "InvalidInviteCode";
    public const string WorkspaceRequired = "WorkspaceRequired";

    public async Task<RegistrationResult> RegisterAsync(RegistrationRequest request)
    {
        var user = new ApplicationUser { UserName = request.Email, Email = request.Email, FullName = request.FullName };
        await using var transaction = await db.Database.BeginTransactionAsync();

        var assignment = await AssignTenantAsync(user, request);
        if (assignment is not null)
        {
            return assignment;
        }

        IdentityResult result;
        try
        {
            result = request.Password is null
                ? await users.CreateAsync(user)
                : await users.CreateAsync(user, request.Password);

            if (result.Succeeded && request.ExternalLogin is not null)
            {
                result = await users.AddLoginAsync(user, request.ExternalLogin);
            }
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            return request.InviteCode is null
                ? RegistrationResult.Failure(nameof(IdentityErrorDescriber.DuplicateEmail), "An account with this email already exists.")
                : RegistrationResult.Failure(InvalidInviteCode, "The invite code is not valid.");
        }

        if (!result.Succeeded)
        {
            return new RegistrationResult(null, result.Errors.ToList());
        }

        await transaction.CommitAsync();
        return new RegistrationResult(user, []);
    }

    private async Task<RegistrationResult?> AssignTenantAsync(ApplicationUser user, RegistrationRequest request)
    {
        var code = request.InviteCode?.Trim();
        if (!string.IsNullOrEmpty(code))
        {
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.MemberInviteCode == code);
            if (tenant is not null)
            {
                user.TenantId = tenant.Id;
                user.Role = TenantRoles.Member;
                return null;
            }

            var student = await db.Students.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.InviteCode == code);
            if (student is null)
            {
                return RegistrationResult.Failure(InvalidInviteCode, "The invite code is not valid.");
            }

            student.InviteCode = null;
            await db.SaveChangesAsync();

            user.TenantId = student.TenantId;
            user.Role = TenantRoles.Student;
            user.StudentId = student.Id;
            return null;
        }

        var workspace = request.WorkspaceName?.Trim();
        if (string.IsNullOrEmpty(workspace))
        {
            return RegistrationResult.Failure(WorkspaceRequired, "Provide a workspace name or an invite code.");
        }

        var created = new Tenant { Name = workspace };
        db.Tenants.Add(created);
        await db.SaveChangesAsync();

        user.TenantId = created.Id;
        user.Role = TenantRoles.Owner;
        return null;
    }
}
