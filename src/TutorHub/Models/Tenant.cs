using System.Security.Cryptography;

namespace TutorHub.Models;

public interface ITenantEntity
{
    int TenantId { get; set; }
}

public enum TenantPlan
{
    Free,
    Premium
}

public class Tenant
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string MemberInviteCode { get; set; } = NewInviteCode();

    public TenantPlan Plan { get; set; } = TenantPlan.Free;

    public static string NewInviteCode() => RandomNumberGenerator.GetHexString(32, lowercase: true);
}
