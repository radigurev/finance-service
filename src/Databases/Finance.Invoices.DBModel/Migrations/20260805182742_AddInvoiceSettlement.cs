using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Invoices.DBModel.Migrations
{
    /// <summary>
    /// Adds the settlement mirror to <c>finance_invoices.Invoices</c> (SDD-INV-001 §2.14, required by
    /// SDD-PAY-002): <c>SettledAmount</c> (<c>DECIMAL(18,2) NOT NULL DEFAULT 0</c>), the string-converted
    /// <c>SettlementStatus</c> (<c>NVARCHAR(20) NOT NULL DEFAULT 'Unsettled'</c> — the SHARED settlement enum
    /// owned by SDD-PAY-002 §2.8, not an invoice-specific fork), the frozen booking rate <c>ExchangeRate</c>
    /// (<c>DECIMAL(18,6) NOT NULL DEFAULT 1.000000</c>) that is the only source of
    /// <c>InvoiceConfirmedEvent.BookingExchangeRate</c>, and the ordering token
    /// <c>LastSettlementAppliedAt</c> (<c>DATETIMEOFFSET NULL</c>, deliberately WITHOUT a
    /// <c>SYSDATETIMEOFFSET()</c> default — it holds the event's <c>OccurredAt</c>, never the row's write
    /// time). Adds <c>IX_Invoices_SettlementStatus</c> for the settlement read patterns.
    /// <para>Purely additive: every existing row is backfilled with <c>SettledAmount = 0.00</c>,
    /// <c>SettlementStatus = 'Unsettled'</c>, <c>ExchangeRate = 1.000000</c> (every pre-existing invoice was
    /// booked in a base-currency context) and <c>LastSettlementAppliedAt = NULL</c> (no settlement event has
    /// been applied yet, so the first one to arrive must win). The already-applied <c>InitialCreate</c>
    /// migration is NOT edited.</para>
    /// </summary>
    public partial class AddInvoiceSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                schema: "finance_invoices",
                table: "Invoices",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 1.000000m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSettlementAppliedAt",
                schema: "finance_invoices",
                table: "Invoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SettledAmount",
                schema: "finance_invoices",
                table: "Invoices",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "SettlementStatus",
                schema: "finance_invoices",
                table: "Invoices",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unsettled");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SettlementStatus",
                schema: "finance_invoices",
                table: "Invoices",
                column: "SettlementStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_SettlementStatus",
                schema: "finance_invoices",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                schema: "finance_invoices",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LastSettlementAppliedAt",
                schema: "finance_invoices",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SettledAmount",
                schema: "finance_invoices",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SettlementStatus",
                schema: "finance_invoices",
                table: "Invoices");
        }
    }
}
