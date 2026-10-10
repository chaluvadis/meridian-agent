using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeridianAgent.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class _20261010104816_InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Profiles",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                Destination = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                TravelerProfile = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                GroupComposition = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                dietary_rules_json = table.Column<string>(type: "TEXT", nullable: false),
                interests_json = table.Column<string>(type: "TEXT", nullable: false),
                avoid_list_json = table.Column<string>(type: "TEXT", nullable: false),
                Pacing = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Budget = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                AttireNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                HomeBase = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                TravelStyle = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                EnergyPattern = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                dietary_restrictions_json = table.Column<string>(type: "TEXT", nullable: false),
                SpiceTolerance = table.Column<int>(type: "INTEGER", nullable: false),
                favorite_cuisines_json = table.Column<string>(type: "TEXT", nullable: false),
                dining_vibe_json = table.Column<string>(type: "TEXT", nullable: false),
                core_interests_json = table.Column<string>(type: "TEXT", nullable: false),
                hard_avoids_json = table.Column<string>(type: "TEXT", nullable: false),
                BudgetComfort = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Mobility = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                TravelGroup = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                FirstTimeVisitor = table.Column<bool>(type: "INTEGER", nullable: false),
                OnboardingCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                PendingWelcome = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Profiles", x => x.Id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Profiles");
    }
}
