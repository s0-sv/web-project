using Microsoft.AspNetCore.Identity;

namespace TutorHub.Models;

public static class TenantRoles
{
    public const string Owner = "Owner";
    public const string Member = "Member";
    public const string Student = "Student";
}

public static class AppPolicies
{
    public const string Staff = "Staff";
    public const string OwnerOnly = "OwnerOnly";
}

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = "";

    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public string Role { get; set; } = TenantRoles.Member;

    public int? StudentId { get; set; }
    public Student? Student { get; set; }
}
