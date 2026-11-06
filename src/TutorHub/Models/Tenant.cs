using System.Security.Cryptography;

namespace TutorHub.Models;

public interface ITenantEntity
{
    int TenantId { get; set; }
}

public class Tenant
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string MemberInviteCode { get; set; } = NewInviteCode();

    public static string NewInviteCode() => RandomNumberGenerator.GetHexString(32, lowercase: true);
}
