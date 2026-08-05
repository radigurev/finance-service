using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Payments.DBModel.Migrations
{
    /// <summary>
    /// ADDITIVE migration adding the SDD-PAY-002 allocation and settlement tables to the
    /// <c>finance_payments</c> database (SDD-PAY-002 §2.12). The already-applied
    /// <c>20260805171727_InitialCreate</c> is NEVER edited.
    /// <para>Creates <c>payments.PaymentAllocations</c> — the sub-ledger match row with an <c>INT IDENTITY</c>
    /// PK, a cascading <c>FK_PaymentAllocations_Payments</c>, <c>DECIMAL(18,2)</c> amounts, a
    /// <c>DATETIMEOFFSET</c> stamp and a <c>rowversion</c> token — with the UNIQUE
    /// <c>IX_PaymentAllocations_PaymentInvoice</c> backstop for <c>PAYMENT_ALLOCATION_DUPLICATE</c>. The
    /// <c>InvoiceId</c> column is a CROSS-SERVICE reference and deliberately carries NO foreign key: the
    /// invoice lives in <c>finance_invoices</c> and a cross-database join is forbidden.</para>
    /// <para>Creates <c>payments.InvoiceOpenItems</c> — the local, event-fed read projection keyed by the
    /// mirrored <c>InvoiceId</c> (never generated) — with exactly the three indexes SDD-PAY-002 §2.12
    /// mandates, including the composite <c>{ Direction, InvoiceStatus, CounterpartyId, DueDate }</c> that
    /// covers the SDD-PAY-003 aging predicate in one seek.</para>
    /// </summary>
    public partial class AddPaymentAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "infrastructure");

            migrationBuilder.CreateTable(
                name: "InvoiceOpenItems",
                schema: "payments",
                columns: table => new
                {
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    BaseCurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    GrossTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BookingExchangeRate = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    IssueDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DueDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InvoiceStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SettledAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    LastAppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceOpenItems", x => x.InvoiceId);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAllocations",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllocatedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BaseAllocatedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RealizedFxDifference = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    AllocatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    AllocatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentAllocations_Payments",
                        column: x => x.PaymentId,
                        principalSchema: "payments",
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceOpenItems_CounterpartyId",
                schema: "payments",
                table: "InvoiceOpenItems",
                column: "CounterpartyId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceOpenItems_Direction_InvoiceStatus_CounterpartyId_DueDate",
                schema: "payments",
                table: "InvoiceOpenItems",
                columns: new[] { "Direction", "InvoiceStatus", "CounterpartyId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceOpenItems_DueDate",
                schema: "payments",
                table: "InvoiceOpenItems",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_InvoiceId",
                schema: "payments",
                table: "PaymentAllocations",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_PaymentId",
                schema: "payments",
                table: "PaymentAllocations",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_PaymentInvoice",
                schema: "payments",
                table: "PaymentAllocations",
                columns: new[] { "PaymentId", "InvoiceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceOpenItems",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "PaymentAllocations",
                schema: "payments");
        }
    }
}
