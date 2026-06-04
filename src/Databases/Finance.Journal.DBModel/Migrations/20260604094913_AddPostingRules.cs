using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Journal.DBModel.Migrations
{
    /// <summary>
    /// Adds the posting-rule reference-data tables (SDD-FIN-006): <c>journal.PostingRules</c>
    /// (<c>INT IDENTITY</c> PK, unique <c>RuleKey</c>, <c>rowversion</c> concurrency token,
    /// <c>DATETIMEOFFSET</c> timestamps) and its composed <c>journal.PostingRuleLines</c>
    /// (enum-as-string <c>DebitOrCredit</c>/<c>AmountSource</c>, reserved <c>DECIMAL(18,6)</c> percentage /
    /// <c>DECIMAL(18,2)</c> fixed-amount columns inert in v1). Seeded from
    /// <c>ICountryStrategy.GetDefaultPostingRules()</c> (SDD-CTRY-001).
    /// </summary>
    public partial class AddPostingRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PostingRules",
                schema: "journal",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RuleKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CountryCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostingRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PostingRuleLines",
                schema: "journal",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PostingRuleId = table.Column<int>(type: "int", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    AccountSelector = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DebitOrCredit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AmountSource = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Percentage = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    FixedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostingRuleLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostingRuleLines_PostingRules",
                        column: x => x.PostingRuleId,
                        principalSchema: "journal",
                        principalTable: "PostingRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PostingRuleLines_PostingRuleId",
                schema: "journal",
                table: "PostingRuleLines",
                column: "PostingRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_PostingRules_CountryCode",
                schema: "journal",
                table: "PostingRules",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "UQ_PostingRules_RuleKey",
                schema: "journal",
                table: "PostingRules",
                column: "RuleKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PostingRuleLines",
                schema: "journal");

            migrationBuilder.DropTable(
                name: "PostingRules",
                schema: "journal");
        }
    }
}
