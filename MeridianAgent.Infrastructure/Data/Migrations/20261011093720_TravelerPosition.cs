#nullable disable
namespace MeridianAgent.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class _20261011093720_TravelerPosition : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "Latitude",
            table: "Profiles",
            type: "REAL",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "Longitude",
            table: "Profiles",
            type: "REAL",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Latitude",
            table: "Profiles");

        migrationBuilder.DropColumn(
            name: "Longitude",
            table: "Profiles");
    }
}
