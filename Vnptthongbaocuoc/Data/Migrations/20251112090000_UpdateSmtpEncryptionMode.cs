using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vnptthongbaocuoc.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSmtpEncryptionMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EncryptionMode",
                table: "SmtpConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql(
                "UPDATE [SmtpConfigurations] SET [EncryptionMode] = CASE WHEN [UseSsl] = 1 THEN 2 ELSE 1 END");

            migrationBuilder.DropColumn(
                name: "UseSsl",
                table: "SmtpConfigurations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UseSsl",
                table: "SmtpConfigurations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE [SmtpConfigurations] SET [UseSsl] = CASE WHEN [EncryptionMode] = 2 THEN 1 ELSE 0 END");

            migrationBuilder.DropColumn(
                name: "EncryptionMode",
                table: "SmtpConfigurations");
        }
    }
}
