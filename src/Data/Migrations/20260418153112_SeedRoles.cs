using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace users_api.src.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Roles"" (""Id"", ""Name"", ""NormalizedName"", ""ConcurrencyStamp"")
                VALUES
                    (gen_random_uuid(), 'Admin', 'ADMIN', gen_random_uuid()::text),
                    (gen_random_uuid(), 'User', 'USER', gen_random_uuid()::text)
                ON CONFLICT DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Roles""
                WHERE ""NormalizedName"" IN ('ADMIN', 'USER');
            ");
        }
    }
}
