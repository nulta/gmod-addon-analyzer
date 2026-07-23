using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GmodAddonAnalyzer.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Addons",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkshopId = table.Column<long>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastFetchedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PackedSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ExtractedSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ModelCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MaterialCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SoundCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LuaCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ModelSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    MaterialSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    SoundSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    LuaSizeBytes = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Addons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AddonId = table.Column<long>(type: "INTEGER", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    SubmittedByIp = table.Column<string>(type: "TEXT", nullable: true),
                    PowNonce = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AddonFiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AddonId = table.Column<long>(type: "INTEGER", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddonFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AddonFiles_Addons_AddonId",
                        column: x => x.AddonId,
                        principalTable: "Addons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LuaAnalyses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AddonFileId = table.Column<long>(type: "INTEGER", nullable: false),
                    Realm = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    RiskLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    RiskSignals = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    HooksDeclared = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    HooksCalled = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    TimersCreated = table.Column<int>(type: "INTEGER", nullable: false),
                    NetMessagesSent = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    NetReceivers = table.Column<int>(type: "INTEGER", nullable: false),
                    Lines = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LuaAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LuaAnalyses_AddonFiles_AddonFileId",
                        column: x => x.AddonFileId,
                        principalTable: "AddonFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AddonFiles_AddonId_RelativePath",
                table: "AddonFiles",
                columns: new[] { "AddonId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Addons_WorkshopId_UpdatedAt",
                table: "Addons",
                columns: new[] { "WorkshopId", "UpdatedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRuns_AddonId",
                table: "AnalysisRuns",
                column: "AddonId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRuns_SubmittedAt",
                table: "AnalysisRuns",
                column: "SubmittedAt");

            migrationBuilder.CreateIndex(
                name: "IX_LuaAnalyses_AddonFileId",
                table: "LuaAnalyses",
                column: "AddonFileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalysisRuns");

            migrationBuilder.DropTable(
                name: "LuaAnalyses");

            migrationBuilder.DropTable(
                name: "AddonFiles");

            migrationBuilder.DropTable(
                name: "Addons");
        }
    }
}
