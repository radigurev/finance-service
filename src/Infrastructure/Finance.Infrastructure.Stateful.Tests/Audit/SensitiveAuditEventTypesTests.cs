using Finance.Infrastructure.Audit;
using NUnit.Framework;

namespace Finance.Infrastructure.Stateful.Tests.Audit;

/// <summary>
/// Unit tests for <see cref="SensitiveAuditEventTypes"/>, the SDD-AUDIT-001 §2 registry of
/// high-sensitivity audit event types that MUST carry a reason. SDD-PAY-001 §2.15 requires the two
/// payment void paths (<c>PaymentCancelled</c>, <c>PaymentReversed</c>) to be members so
/// <c>AuditService</c> independently rejects a reasonless entry; adding the constant alone is not
/// enough, because <see cref="SensitiveAuditEventTypes.RequiresReason(string)"/> tests a private set
/// the constant must also be added to.
/// </summary>
[TestFixture]
[Category("SDD-AUDIT-001")]
[Category("SDD-PAY-001")]
public sealed class SensitiveAuditEventTypesTests
{
    [Test]
    public void RequiresReason_PaymentCancelled_ReturnsTrue()
    {
        // Arrange
        string eventType = SensitiveAuditEventTypes.PaymentCancelled;

        // Act
        bool requiresReason = SensitiveAuditEventTypes.RequiresReason(eventType);

        // Assert
        Assert.That(requiresReason, Is.True);
    }

    [Test]
    public void RequiresReason_PaymentReversed_ReturnsTrue()
    {
        // Arrange
        string eventType = SensitiveAuditEventTypes.PaymentReversed;

        // Act
        bool requiresReason = SensitiveAuditEventTypes.RequiresReason(eventType);

        // Assert
        Assert.That(requiresReason, Is.True);
    }

    [Test]
    public void RequiresReason_PreExistingSensitiveEventTypes_RemainTrue()
    {
        // Arrange
        string[] preExisting =
        [
            SensitiveAuditEventTypes.PeriodClosed,
            SensitiveAuditEventTypes.FiscalPeriodClosed,
            SensitiveAuditEventTypes.FiscalPeriodReopened,
            SensitiveAuditEventTypes.JournalEntryReversed,
            SensitiveAuditEventTypes.AccountDeactivated,
            SensitiveAuditEventTypes.PermissionRevoked
        ];

        // Act
        bool allRequireReason = System.Array.TrueForAll(
            preExisting,
            SensitiveAuditEventTypes.RequiresReason);

        // Assert
        Assert.That(allRequireReason, Is.True);
    }

    [Test]
    public void RequiresReason_NonSensitivePaymentEventTypes_ReturnsFalse()
    {
        // Arrange
        string paymentConfirmed = "PaymentConfirmed";
        string paymentPosted = "PaymentPosted";
        string paymentAllocated = "PaymentAllocated";

        // Act
        bool confirmedRequiresReason = SensitiveAuditEventTypes.RequiresReason(paymentConfirmed);
        bool postedRequiresReason = SensitiveAuditEventTypes.RequiresReason(paymentPosted);
        bool allocatedRequiresReason = SensitiveAuditEventTypes.RequiresReason(paymentAllocated);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(confirmedRequiresReason, Is.False);
            Assert.That(postedRequiresReason, Is.False);
            Assert.That(allocatedRequiresReason, Is.False);
        });
    }

    [Test]
    public void RequiresReason_MatchesCaseSensitively()
    {
        // Arrange
        string wrongCase = "paymentcancelled";

        // Act
        bool requiresReason = SensitiveAuditEventTypes.RequiresReason(wrongCase);

        // Assert
        Assert.That(requiresReason, Is.False);
    }
}
