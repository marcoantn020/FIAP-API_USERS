namespace users_api.Common;

public static class UserRole
{
    public const string Admin = "Admin";
    public const string User = "User";

    public static readonly string[] AllRoles = { Admin, User };
}
