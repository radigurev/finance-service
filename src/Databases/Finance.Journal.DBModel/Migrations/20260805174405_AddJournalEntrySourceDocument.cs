using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Journal.DBModel.Migrations
{
    /// <summary>
    /// Adds the duplicate-post backstop to <c>journal.JournalEntries</c> (SDD-PAY-001 §2.5, §2.16; additive to
    /// SDD-FIN-002): the nullable <c>SourceDocumentType</c> (<c>NVARCHAR(40)</c>) / <c>SourceDocumentId</c>
    /// (<c>UNIQUEIDENTIFIER</c>) pair identifying the financial document an entry was posted for, plus the
    /// UNIQUE FILTERED index <c>IX_JournalEntries_SourceDocument</c> admitting at most one <c>Posted</c> entry
    /// per source document.
    /// <para>Both columns are nullable with no default, so this is a pure <c>AddColumn</c> + <c>CreateIndex</c>
    /// against existing rows: every pre-existing entry keeps both columns NULL and is exempt from the filtered
    /// index, as are drafts, reversing entries, and manually created entries.</para>
    /// </summary>
    public partial class AddJournalEntrySourceDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceDocumentId",
                schema: "journal",
                table: "JournalEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceDocumentType",
                schema: "journal",
                table: "JournalEntries",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_SourceDocument",
                schema: "journal",
                table: "JournalEntries",
                columns: new[] { "SourceDocumentType", "SourceDocumentId" },
                unique: true,
                filter: "[SourceDocumentType] IS NOT NULL AND [SourceDocumentId] IS NOT NULL AND [Status] = 'Posted'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_SourceDocument",
                schema: "journal",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "SourceDocumentId",
                schema: "journal",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "SourceDocumentType",
                schema: "journal",
                table: "JournalEntries");
        }
    }
}
