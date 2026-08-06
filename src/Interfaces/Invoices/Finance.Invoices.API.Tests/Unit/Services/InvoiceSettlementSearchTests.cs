using Finance.Common.Enums;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Invoices.API.Tests.Builders;
using Finance.Invoices.API.Tests.Fixtures;
using Finance.Invoices.DBModel.Models;
using Finance.ServiceModel.Invoices;
using NUnit.Framework;

namespace Finance.Invoices.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the settlement half of the invoice list surface (SDD-INV-001 §2.10/§2.14, §6.7). The
/// <c>[Filterable]</c>/<c>[Sortable]</c> opt-in on <c>SettledAmount</c> and <c>SettlementStatus</c> is what makes
/// "show me everything still outstanding" a single indexed query instead of a client-side scan, so the tests run
/// the SHIPPED SDD-INFRA-005 pipeline through <c>InvoiceService.SearchAsync</c> against the SQLite in-memory
/// context and assert the SERVER-SIDE outcome: <c>TotalCount</c> is computed on the filtered query BEFORE paging,
/// so a filter that never reached SQL could not produce it.
/// </summary>
[TestFixture]
[Category("SDD-INV-001")]
[Category("SDD-PAY-002")]
[Category("SDD-INFRA-005")]
public sealed class InvoiceSettlementSearchTests
{
    private SqliteInvoicesDbContextScope _scope = null!;
    private InvoiceServiceTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteInvoicesDbContextFactory.Create();
        _harness = InvoiceServiceTestHarness.Build(_scope.Context);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    [Test]
    public async Task Search_FiltersBySettlementStatus_ServerSide()
    {
        // Arrange
        await SeedThreeSettlementStatesAsync();
        FilterRequest request = new()
        {
            Page = 1,
            PageSize = 50,
            Filters =
            [
                new FilterCriterion
                {
                    Field = nameof(Invoice.SettlementStatus),
                    Operator = "eq",
                    Value = nameof(SettlementStatus.PartiallySettled)
                }
            ]
        };

        // Act
        Result<PagedResult<InvoiceDto>> result =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        PagedResult<InvoiceDto> page = result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(
                page.TotalCount,
                Is.EqualTo(1),
                "TotalCount is counted on the filtered query, so the predicate reached the database");
            Assert.That(page.Items, Has.Count.EqualTo(1));
            Assert.That(page.Items[0].DocumentNumber, Is.EqualTo("SINV-2026-000002"));
            Assert.That(page.Items[0].SettlementStatus, Is.EqualTo(SettlementStatus.PartiallySettled));
            Assert.That(page.Items[0].SettledAmount, Is.EqualTo(400.00m));
        });
    }

    [Test]
    public async Task Search_FiltersBySettledAmountRange_ServerSide()
    {
        // Arrange
        await SeedThreeSettlementStatesAsync();
        FilterRequest request = new()
        {
            Page = 1,
            PageSize = 50,
            Filters =
            [
                new FilterCriterion
                {
                    Field = nameof(Invoice.SettledAmount),
                    Operator = "gt",
                    Value = "0"
                }
            ]
        };

        // Act
        Result<PagedResult<InvoiceDto>> result =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        PagedResult<InvoiceDto> page = result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(page.TotalCount, Is.EqualTo(2));
            Assert.That(
                page.Items.Select(invoice => invoice.SettledAmount),
                Is.All.GreaterThan(0m));
        });
    }

    [Test]
    public async Task Search_SortsBySettledAmountDescending_ServerSide()
    {
        // Arrange
        await SeedThreeSettlementStatesAsync();
        FilterRequest request = new()
        {
            Page = 1,
            PageSize = 50,
            Sort = [new SortCriterion { Field = nameof(Invoice.SettledAmount), Direction = "desc" }]
        };

        // Act
        Result<PagedResult<InvoiceDto>> result =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.That(
            result.Value!.Items.Select(invoice => invoice.SettledAmount),
            Is.EqualTo(new[] { 1000.00m, 400.00m, 0.00m }));
    }

    /// <summary>
    /// Seeds one unsettled, one partially settled, and one fully settled posted invoice, each with its own
    /// document number so the UNIQUE filtered index is not violated.
    /// </summary>
    /// <returns>A task completing when the three invoices are persisted.</returns>
    private async Task SeedThreeSettlementStatesAsync()
    {
        _scope.Context.Invoices.AddRange(
            InvoiceSeedBuilder.Create()
                .WithDocumentNumber("SINV-2026-000001")
                .WithGrossTotal(1000.00m)
                .WithSettlement(0.00m, SettlementStatus.Unsettled, null)
                .Build(),
            InvoiceSeedBuilder.Create()
                .WithDocumentNumber("SINV-2026-000002")
                .WithGrossTotal(1000.00m)
                .WithSettlement(400.00m, SettlementStatus.PartiallySettled, null)
                .Build(),
            InvoiceSeedBuilder.Create()
                .WithDocumentNumber("SINV-2026-000003")
                .WithGrossTotal(1000.00m)
                .WithSettlement(1000.00m, SettlementStatus.Settled, null)
                .Build());

        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        _scope.Context.ChangeTracker.Clear();
    }
}
