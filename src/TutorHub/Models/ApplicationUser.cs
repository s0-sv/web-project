using Microsoft.AspNetCore.Identity;

namespace TutorHub.Models;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = "";
}
