using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using System;

namespace TinkerGenie.API.Migrations
{
    public partial class AddUserLearningTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create user_skill_progression table
            migrationBuilder.CreateTable(
                name: "user_skill_progression",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false),
                    user_id = table.Column<Guid>(nullable: false),
                    skill_domain = table.Column<string>(nullable: false),
                    skill_level = table.Column<double>(nullable: false),
                    measured_at = table.Column<DateTime>(nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_skill_progression", x => x.id);
                });

            // Create index on user_skill_progression
            migrationBuilder.CreateIndex(
                name: "IX_user_skill_progression_user_id_skill_domain",
                table: "user_skill_progression",
                columns: new[] { "user_id", "skill_domain" });

            // Create user_learning_goals table
            migrationBuilder.CreateTable(
                name: "user_learning_goals",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false),
                    user_id = table.Column<Guid>(nullable: false),
                    learning_goal = table.Column<string>(maxLength: 500, nullable: false),
                    progress_notes = table.Column<string>(maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    target_completion_date = table.Column<DateTime>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_learning_goals", x => x.id);
                });

            // Create index on user_learning_goals
            migrationBuilder.CreateIndex(
                name: "IX_user_learning_goals_user_id",
                table: "user_learning_goals",
                column: "user_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop indexes
            migrationBuilder.DropIndex(
                name: "IX_user_skill_progression_user_id_skill_domain",
                table: "user_skill_progression");

            migrationBuilder.DropIndex(
                name: "IX_user_learning_goals_user_id",
                table: "user_learning_goals");

            // Drop tables
            migrationBuilder.DropTable(
                name: "user_skill_progression");

            migrationBuilder.DropTable(
                name: "user_learning_goals");
        }
    }
}



