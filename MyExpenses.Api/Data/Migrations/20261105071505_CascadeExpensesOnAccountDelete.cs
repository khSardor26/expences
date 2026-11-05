using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyExpenses.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CascadeExpensesOnAccountDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Categories_UserId_CategoryId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Items_UserId_ItemId",
                table: "Expenses");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_AspNetUsers_UserId",
                table: "Expenses",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Categories_UserId_CategoryId",
                table: "Expenses",
                columns: new[] { "UserId", "CategoryId" },
                principalTable: "Categories",
                principalColumns: new[] { "UserId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Items_UserId_ItemId",
                table: "Expenses",
                columns: new[] { "UserId", "ItemId" },
                principalTable: "Items",
                principalColumns: new[] { "UserId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_AspNetUsers_UserId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Categories_UserId_CategoryId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Items_UserId_ItemId",
                table: "Expenses");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Categories_UserId_CategoryId",
                table: "Expenses",
                columns: new[] { "UserId", "CategoryId" },
                principalTable: "Categories",
                principalColumns: new[] { "UserId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Items_UserId_ItemId",
                table: "Expenses",
                columns: new[] { "UserId", "ItemId" },
                principalTable: "Items",
                principalColumns: new[] { "UserId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
