using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResolveIQ.Api.Migrations
{
    /// <inheritdoc />
    public partial class MakeTicketHistoryUserIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TicketHistories_Users_UserId",
                table: "TicketHistories");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "TicketHistories",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddForeignKey(
                name: "FK_TicketHistories_Users_UserId",
                table: "TicketHistories",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TicketHistories_Users_UserId",
                table: "TicketHistories");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "TicketHistories",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TicketHistories_Users_UserId",
                table: "TicketHistories",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
