using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FpaiConnect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreferencesAndSelfRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalNote",
                table: "Users",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedById",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ColorScheme",
                table: "Users",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FontChoice",
                table: "Users",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RegistrationNote",
                table: "Users",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ThemeMode",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Users_ApprovedById",
                table: "Users",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Status",
                table: "Users",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Users_ApprovedById",
                table: "Users",
                column: "ApprovedById",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Users_ApprovedById",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_ApprovedById",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Status",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ApprovalNote",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ApprovedById",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ColorScheme",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FontChoice",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RegistrationNote",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ThemeMode",
                table: "Users");
        }
    }
}
