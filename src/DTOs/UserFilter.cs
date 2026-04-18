namespace users_api.DTOs;

public class UserFilterRequest
{
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? Role { get; set; }
    public DateTime? CreatedAfter { get; set; }
    public DateTime? CreatedBefore { get; set; }
}
