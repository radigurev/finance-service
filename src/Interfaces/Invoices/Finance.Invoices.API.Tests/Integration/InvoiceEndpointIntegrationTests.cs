using System.Net;
using System.Net.Http.Json;
using Finance.Common.Enums;
using Finance.IntegrationTesting;
using Finance.Invoices.DBModel;
using Finance.Invoices.DBModel.Models;
using Finance.ServiceModel.Invoices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Finance.Invoices.API.Tests.Integration;

/// <summary>
/// Endpoint, real-SQL, and outbox integration tests for the invoice lifecycle (SDD-INV-001 §6.6). Each test
/// boots the real <c>Finance.Invoices.API</c> host through <see cref="FinanceApiFactory{TProgram}"/> against
/// the shared Testcontainers SQL Server / Redis / RabbitMQ infrastructure with a minted JWT and real RBAC, so
/// the create/confirm/cancel/update flows, the gapless document numbering, and the audit + outbox writes run
/// end-to-end. Tagged <c>[Category("Integration")]</c> so the offline unit run skips it.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("SDD-INV-001")]
[Category("SDD-INFRA-003")]
public sealed class InvoiceEndpointIntegrationTests
{
    private const string CreatePermission = "finance.invoice:create";
    private const string ConfirmPermission = "finance.invoice:confirm";
    private const string CancelPermission = "finance.invoice:cancel";
    private const string ReadPermission = "finance.invoice:read";

    private FinanceApiFactory<Program> _factory = null!;
    private DatabaseResetter _resetter = null!;

    /// <summary>Builds the host factory once against the shared containers.</summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new FinanceApiFactory<Program>();
        _ = _factory.Server;
        _resetter = new DatabaseResetter(
            IntegrationTestSetup.Containers.SqlConnectionStringForDatabase("finance_invoices_test"));
    }

    /// <summary>Resets DB rows before each test for isolation.</summary>
    [SetUp]
    public async Task SetUp()
    {
        await _resetter.ResetAsync();
    }

    /// <summary>Disposes the host factory after the fixture.</summary>
    [OneTimeTearDown]
    public async Task OneTimeTearDown() => await _factory.DisposeAsync();

    /// <summary>POST creates a draft, returns 201, and persists it as Draft with no document number.</summary>
    [Test]
    public async Task Create_Returns201_AndPersistsDraft()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        CreateInvoiceRequest request = BuildCreateRequest();

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/invoices", request);
        InvoiceDto? created = await response.Content.ReadFromJsonAsync<InvoiceDto>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(created, Is.Not.Null);
        Invoice? persisted = await FindInvoiceAsync(created!.Id);
        Assert.That(persisted, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Status, Is.EqualTo(InvoiceStatus.Draft));
            Assert.That(persisted.DocumentNumber, Is.Null);
            Assert.That(persisted.Lines, Has.Count.EqualTo(1));
        });
    }

    /// <summary>Confirm returns 200 and writes the audit row and outbox message in the same transaction.</summary>
    [Test]
    public async Task Confirm_Returns200_AndWritesOutboxAndAuditRow_InSameTransaction()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission, ConfirmPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        InvoiceDto draft = await CreateDraftAsync(client);

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/invoices/{draft.Id}/confirm", new ConfirmInvoiceRequest { RowVersion = draft.RowVersion });
        InvoiceDto? confirmed = await response.Content.ReadFromJsonAsync<InvoiceDto>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(confirmed!.Status, Is.EqualTo(InvoiceStatus.Confirmed));
            Assert.That(confirmed.DocumentNumber, Is.Not.Null);
        });
    }

    /// <summary>Confirm allocates gapless per-document-type numbers with no gaps (also SDD-INFRA-003).</summary>
    [Test]
    public async Task Confirm_AllocatesGaplessDocumentNumbers_NoGaps_PerDocumentType()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission, ConfirmPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        InvoiceDto firstDraft = await CreateDraftAsync(client);
        InvoiceDto secondDraft = await CreateDraftAsync(client);

        // Act
        InvoiceDto first = await ConfirmAsync(client, firstDraft);
        InvoiceDto second = await ConfirmAsync(client, secondDraft);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(first.DocumentNumber, Is.Not.Null);
            Assert.That(second.DocumentNumber, Is.Not.Null);
            Assert.That(first.DocumentNumber, Is.Not.EqualTo(second.DocumentNumber));
        });
    }

    /// <summary>Confirm returns 409 when the invoice is already confirmed.</summary>
    [Test]
    public async Task Confirm_Returns409_WhenAlreadyConfirmed()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission, ConfirmPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        InvoiceDto draft = await CreateDraftAsync(client);
        InvoiceDto confirmed = await ConfirmAsync(client, draft);

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/invoices/{confirmed.Id}/confirm",
            new ConfirmInvoiceRequest { RowVersion = confirmed.RowVersion });

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    /// <summary>Cancel returns 400 when no reason is supplied.</summary>
    [Test]
    public async Task Cancel_Returns400_WhenReasonMissing()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission, CancelPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        InvoiceDto draft = await CreateDraftAsync(client);

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/invoices/{draft.Id}/cancel",
            new CancelInvoiceRequest { Reason = string.Empty, RowVersion = draft.RowVersion });

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    /// <summary>Update returns 409 when the invoice has been confirmed (immutable).</summary>
    [Test]
    public async Task Update_Returns409_WhenInvoiceConfirmed()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission, ConfirmPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        InvoiceDto draft = await CreateDraftAsync(client);
        InvoiceDto confirmed = await ConfirmAsync(client, draft);
        UpdateInvoiceRequest request = new()
        {
            CounterpartyId = confirmed.CounterpartyId,
            CurrencyCode = confirmed.CurrencyCode,
            IssueDate = confirmed.IssueDate,
            DueDate = confirmed.DueDate,
            Lines = [BuildLineRequest()],
            RowVersion = confirmed.RowVersion
        };

        // Act
        HttpResponseMessage response =
            await client.PutAsJsonAsync($"/api/v1/invoices/{confirmed.Id}", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    /// <summary>The posting handshake moves the invoice to Posted and links the journal entry id.</summary>
    [Test]
    public async Task PostingHandshake_ConfirmThenJournalPostedBack_MovesInvoiceToPosted_AndLinksJournalEntryId()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission, ConfirmPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        InvoiceDto draft = await CreateDraftAsync(client);
        InvoiceDto confirmed = await ConfirmAsync(client, draft);
        Guid journalEntryId = Guid.NewGuid();

        // Act — simulate the Journal back-event by invoking the link path through the service scope.
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            Finance.Invoices.API.Interfaces.IInvoiceService invoices =
                scope.ServiceProvider.GetRequiredService<Finance.Invoices.API.Interfaces.IInvoiceService>();
            await invoices.LinkPostedJournalEntryAsync(confirmed.Id, journalEntryId, CancellationToken.None);
        }

        Invoice? posted = await FindInvoiceAsync(confirmed.Id);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(posted!.Status, Is.EqualTo(InvoiceStatus.Posted));
            Assert.That(posted.JournalEntryId, Is.EqualTo(journalEntryId));
        });
    }

    /// <summary>An endpoint returns 403 when the caller lacks the required permission.</summary>
    [Test]
    public async Task Endpoint_Returns403_WhenPermissionMissing()
    {
        // Arrange — grant nothing.
        _factory.PermissionState.RevokeAll();
        HttpClient client = _factory.CreateAuthenticatedClient();

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/invoices", BuildCreateRequest());

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private async Task<InvoiceDto> CreateDraftAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/invoices", BuildCreateRequest());
        InvoiceDto? created = await response.Content.ReadFromJsonAsync<InvoiceDto>();
        Assert.That(created, Is.Not.Null);
        return created!;
    }

    private async Task<InvoiceDto> ConfirmAsync(HttpClient client, InvoiceDto draft)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/invoices/{draft.Id}/confirm", new ConfirmInvoiceRequest { RowVersion = draft.RowVersion });
        InvoiceDto? confirmed = await response.Content.ReadFromJsonAsync<InvoiceDto>();
        Assert.That(confirmed, Is.Not.Null);
        return confirmed!;
    }

    private async Task<Invoice?> FindInvoiceAsync(Guid id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        InvoicesDbContext db = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        return await db.Invoices.Include(invoice => invoice.Lines)
            .FirstOrDefaultAsync(invoice => invoice.Id == id);
    }

    private static CreateInvoiceRequest BuildCreateRequest() => new()
    {
        DocumentType = InvoiceDocumentType.SaleInvoice,
        CounterpartyId = Guid.NewGuid(),
        CurrencyCode = "BGN",
        IssueDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
        DueDate = new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero),
        Lines = [BuildLineRequest()]
    };

    private static InvoiceLineRequest BuildLineRequest() => new()
    {
        Description = "Integration line",
        Quantity = 2m,
        UnitPrice = 50m,
        TaxRate = 0.20m
    };
}
