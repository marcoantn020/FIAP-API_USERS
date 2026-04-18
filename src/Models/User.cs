using Microsoft.AspNetCore.Identity;
using users_api.Common;

namespace users_api.Models;

public class User : IdentityUser<Guid>, IAuditable
{
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}