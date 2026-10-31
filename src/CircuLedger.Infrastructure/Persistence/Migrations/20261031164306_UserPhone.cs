using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CircuLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserPhone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "phone_encrypted",
                table: "AspNetUsers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone_last4",
                table: "AspNetUsers",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            // Registered citizens already have their phone on the linked citizen row, encrypted with the same key.
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" u
                SET phone_encrypted = c.phone_encrypted, phone_last4 = c.phone_last4
                FROM (
                    SELECT DISTINCT ON (user_id) user_id, phone_encrypted, phone_last4
                    FROM citizens
                    WHERE user_id IS NOT NULL AND is_deleted = false
                    ORDER BY user_id, created_at DESC
                ) c
                WHERE c.user_id = u.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "phone_encrypted",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "phone_last4",
                table: "AspNetUsers");
        }
    }
}
