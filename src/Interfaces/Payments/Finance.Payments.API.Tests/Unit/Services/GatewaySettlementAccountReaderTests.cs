using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Payments.API.Interfaces;
using Finance.Payments.API.Services;
using Finance.ServiceModel.Accounts;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="GatewaySettlementAccountReader"/> (SDD-PAY-001 §2.8, §6.3): the settlement account is
/// asserted through the Accounts read seam, an inactive account is blocked, an unreachable Accounts service FAILS
/// CLOSED, and the DORMANT <c>IsPostable</c> strictness predicate returns <c>true</c> unconditionally until
/// CHG-ENH-002 lands.
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
public sealed class GatewaySettlementAccountReaderTests
{
    private Mock<IAccountReadClient> _accountsMock = null!;
    private GatewaySettlementAccountReader _sut = null!;

    /// <summary>Creates a fresh mock and reader before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _accountsMock = new Mock<IAccountReadClient>();
        _sut = new GatewaySettlementAccountReader(
            _accountsMock.Object,
            NullLogger<GatewaySettlementAccountReader>.Instance);
    }

    [Test]
    public async Task EnsureUsable_ActiveAccount_ReturnsSuccess()
    {
        // Arrange
        _accountsMock
            .Setup(client => client.GetAccountAsync(503, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountFor(isActive: true));

        // Act
        Result result = await _sut.EnsureUsableAsync(503, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
    }

    [Test]
    public async Task SettlementAccount_Inactive_ReturnsPaymentSettlementAccountInactive()
    {
        // Arrange
        _accountsMock
            .Setup(client => client.GetAccountAsync(503, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountFor(isActive: false));

        // Act
        Result result = await _sut.EnsureUsableAsync(503, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE));
        });
    }

    [Test]
    public async Task SettlementAccount_ReaderUnreachable_FailsClosed_ReturnsPaymentSettlementAccountNotFound()
    {
        // Arrange
        _accountsMock
            .Setup(client => client.GetAccountAsync(503, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("The Accounts service is unreachable."));

        // Act
        Result result = await _sut.EnsureUsableAsync(503, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.ErrorCode,
                Is.EqualTo(PaymentErrorCodes.PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND),
                "financial safety over availability");
        });
    }

    [Test]
    public void EnsureUsable_Cancelled_RethrowsOperationCanceled()
    {
        // Arrange
        _accountsMock
            .Setup(client => client.GetAccountAsync(503, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        Assert.That(
            async () => await _sut.EnsureUsableAsync(503, CancellationToken.None),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void SettlementAccountReader_IsPostable_ReturnsTrueUnconditionally_UntilChgEnh002Lands()
    {
        // Arrange
        AccountDto inactiveHeaderLikeAccount = AccountFor(isActive: false);
        AccountDto activeLeafLikeAccount = AccountFor(isActive: true);

        // Act
        bool inactiveIsPostable = _sut.IsPostable(inactiveHeaderLikeAccount);
        bool activeIsPostable = _sut.IsPostable(activeLeafLikeAccount);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(inactiveIsPostable, Is.True, "the strictness seam is DORMANT in v1");
            Assert.That(activeIsPostable, Is.True);
        });
    }

    /// <summary>Builds a chart-of-accounts DTO for the reader under test.</summary>
    /// <param name="isActive">Whether the account is active.</param>
    /// <returns>The account DTO.</returns>
    private static AccountDto AccountFor(bool isActive) => new()
    {
        Id = 503,
        Code = "503",
        Name = "Bank",
        Type = AccountType.Asset,
        ParentId = null,
        IsActive = isActive,
        CountryCode = "BG",
        RowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })
    };
}
