using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FpaiConnect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMicrosoftSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MicrosoftSubjectId",
                table: "Users",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_MicrosoftSubjectId",
                table: "Users",
                column: "MicrosoftSubjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_MicrosoftSubjectId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MicrosoftSubjectId",
                table: "Users");
        }
    }
}
