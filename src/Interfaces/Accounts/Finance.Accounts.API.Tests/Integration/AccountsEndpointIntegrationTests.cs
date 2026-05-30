using NUnit.Framework;

namespace Finance.Accounts.API.Tests.Integration;

/// <summary>
/// Placeholders for the SDD-ACCT-001 §6.2 endpoint &amp; wiring tests. These require a full
/// <c>WebApplicationFactory&lt;Program&gt;</c> host with the shared auth-service (real
/// <c>[RequirePermission]</c> JWT checks), SQL Server, Redis, and RabbitMQ — none of which are available
/// in the offline build environment. They are tagged <c>[Category("Integration")]</c> and excluded from
/// the default run (<c>TestCategory!=Integration</c>); each is <c>[Ignore]</c>d with the blocking reason.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("SDD-ACCT-001")]
public sealed class AccountsEndpointIntegrationTests
{
    private const string OfflineReason =
        "Requires WebApplicationFactory host + auth-service/SQL/Redis/RabbitMQ — unavailable offline.";

    /// <summary>List returns an empty paged result when no accounts exist.</summary>
    [Test]
    [Ignore(OfflineReason)]
    public void List_ReturnsEmptyPagedResult_WhenNoAccounts() => Assert.Pass();

    /// <summary>List returns the paged result ordered by CountryCode then Code.</summary>
    [Test]
    [Ignore(OfflineReason)]
    public void List_ReturnsPagedResultOrderedByCountryAndCode() => Assert.Pass();

    /// <summary>List applies the filter and sort supplied via the query string.</summary>
    [Test]
    [Ignore(OfflineReason)]
    public void List_AppliesFilterAndSortFromQueryString() => Assert.Pass();

    /// <summary>Get returns a 404 ProblemDetails when the account does not exist.</summary>
    [Test]
    [Ignore(OfflineReason)]
    public void Get_Returns404ProblemDetails_WhenAccountDoesNotExist() => Assert.Pass();

    /// <summary>Get returns the account when it exists.</summary>
    [Test]
    [Ignore(OfflineReason)]
    public void Get_ReturnsAccount_WhenExists() => Assert.Pass();

    /// <summary>Create returns 201 and persists the account.</summary>
    [Test]
    [Ignore(OfflineReason)]
    public void Create_Returns201_AndPersistsAccount() => Assert.Pass();

    /// <summary>Create returns a 400 ProblemDetails when the code is missing.</summary>
    [Test]
    [Ignore(OfflineReason)]
    public void Create_Returns400ProblemDetails_WhenCodeMissing() => Assert.Pass();

    /// <summary>Create returns a 400 ProblemDetails when the type is invalid.</summary>
    [Test]
    [Ignore(OfflineReason)]
    public void Create_Returns400ProblemDetails_WhenTypeInvalid() => Assert.Pass();

    /// <summary>Create returns a 409 ProblemDetails when the code duplicates an existing one.</summary>
    [Test]
    [Ignore(OfflineReason)]
    public void Create_Returns409ProblemDetails_WhenDuplicateCodeInSameCountry() => Assert.Pass();

    /// <summary>Create returns a 400 ProblemDetails when the parent does not exist.</summary>
    [Test]
    [Ignore(OfflineReason)]
    public void Create_Returns400ProblemDetails_WhenParentDoesNotExist() => Assert.Pass();

    /// <summary>Create writes the outbox message and audit row in the same transaction.</summary>
    [Test]
    [Ignore(OfflineReason)]
    public void Create_WritesOutboxMessageAndAuditRow_InSameTransaction() => Assert.Pass();

    /// <summary>Update changes Name and IsActive without changing immutable fields.</summary>
    [Test]
    [Ignore(OfflineReason)]
    public void Update_ChangesNameAndIsActive_DoesNotChangeImmutableFields() => Assert.Pass();

    /// <summary>Update returns a 404 ProblemDetails when the account does not exist.</summary>
    [Test]
    [Ignore(OfflineReason)]
    public void Update_Returns404ProblemDetails_WhenAccountDoesNotExist() => Assert.Pass();

    /// <summary>Update returns a 409 ProblemDetails when the row version is stale.</summary>
    [Test]
    [Ignore(OfflineReason)]
    public void Update_Returns409ProblemDetails_WhenRowVersionStale() => Assert.Pass();

    /// <summary>An endpoint returns 403 when the caller lacks the required permission.</summary>
    [Test]
    [Ignore(OfflineReason)]
    public void Endpoint_Returns403_WhenPermissionMissing() => Assert.Pass();
}
