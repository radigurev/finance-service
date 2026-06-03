using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Periods.DBModel.Migrations
{
    /// <summary>
    /// Initial schema for the Periods microservice (database <c>finance_periods</c>). Creates the
    /// <c>periods.FiscalPeriods</c> aggregate (INT IDENTITY PK + <c>rowversion</c>, unique
    /// <c>(FiscalYear, PeriodNumber)</c>) and its append-only <c>FiscalPeriodStatusHistory</c>, the
    /// <c>audit.OperationsEvents</c> trail (SDD-AUDIT-001), and the MassTransit transactional-outbox tables
    /// (SDD-INFRA-006). Implements SDD-FIN-004 §2-§3.
    /// </summary>
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "periods");

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "FiscalPeriods",
                schema: "periods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    PeriodNumber = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReopenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReopenedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalPeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InboxState",
                schema: "periods",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LockId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Received = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceiveCount = table.Column<int>(type: "int", nullable: false),
                    ExpirationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Consumed = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Delivered = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxState", x => x.Id);
                    table.UniqueConstraint("AK_InboxState_MessageId_ConsumerId", x => new { x.MessageId, x.ConsumerId });
                });

            migrationBuilder.CreateTable(
                name: "OperationsEvents",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationsEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxState",
                schema: "periods",
                columns: table => new
                {
                    OutboxId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LockId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Delivered = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxState", x => x.OutboxId);
                });

            migrationBuilder.CreateTable(
                name: "FiscalPeriodStatusHistory",
                schema: "periods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FiscalPeriodId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalPeriodStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FiscalPeriodStatusHistory_FiscalPeriods",
                        column: x => x.FiscalPeriodId,
                        principalSchema: "periods",
                        principalTable: "FiscalPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessage",
                schema: "periods",
                columns: table => new
                {
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EnqueueTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Headers = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InboxMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InboxConsumerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OutboxId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InitiatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DestinationAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ResponseAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FaultAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ExpirationTime = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessage", x => x.SequenceNumber);
                    table.ForeignKey(
                        name: "FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId",
                        columns: x => new { x.InboxMessageId, x.InboxConsumerId },
                        principalSchema: "periods",
                        principalTable: "InboxState",
                        principalColumns: new[] { "MessageId", "ConsumerId" });
                    table.ForeignKey(
                        name: "FK_OutboxMessage_OutboxState_OutboxId",
                        column: x => x.OutboxId,
                        principalSchema: "periods",
                        principalTable: "OutboxState",
                        principalColumn: "OutboxId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriods_FiscalYear_PeriodNumber",
                schema: "periods",
                table: "FiscalPeriods",
                columns: new[] { "FiscalYear", "PeriodNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriods_StartDate_EndDate",
                schema: "periods",
                table: "FiscalPeriods",
                columns: new[] { "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriods_Status",
                schema: "periods",
                table: "FiscalPeriods",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriodStatusHistory_FiscalPeriodId",
                schema: "periods",
                table: "FiscalPeriodStatusHistory",
                column: "FiscalPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_InboxState_Delivered",
                schema: "periods",
                table: "InboxState",
                column: "Delivered");

            migrationBuilder.CreateIndex(
                name: "IX_OperationsEvents_CorrelationId",
                schema: "audit",
                table: "OperationsEvents",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationsEvents_EntityType_EntityId",
                schema: "audit",
                table: "OperationsEvents",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationsEvents_OccurredAt",
                schema: "audit",
                table: "OperationsEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_EnqueueTime",
                schema: "periods",
                table: "OutboxMessage",
                column: "EnqueueTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_ExpirationTime",
                schema: "periods",
                table: "OutboxMessage",
                column: "ExpirationTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber",
                schema: "periods",
                table: "OutboxMessage",
                columns: new[] { "InboxMessageId", "InboxConsumerId", "SequenceNumber" },
                unique: true,
                filter: "[InboxMessageId] IS NOT NULL AND [InboxConsumerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_OutboxId_SequenceNumber",
                schema: "periods",
                table: "OutboxMessage",
                columns: new[] { "OutboxId", "SequenceNumber" },
                unique: true,
                filter: "[OutboxId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxState_Created",
                schema: "periods",
                table: "OutboxState",
                column: "Created");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FiscalPeriodStatusHistory",
                schema: "periods");

            migrationBuilder.DropTable(
                name: "OperationsEvents",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "OutboxMessage",
                schema: "periods");

            migrationBuilder.DropTable(
                name: "FiscalPeriods",
                schema: "periods");

            migrationBuilder.DropTable(
                name: "InboxState",
                schema: "periods");

            migrationBuilder.DropTable(
                name: "OutboxState",
                schema: "periods");
        }
    }
}
