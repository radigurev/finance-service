using Finance.Common.ErrorCodes;
using NUnit.Framework;

namespace Finance.Invoices.API.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="InvoiceErrorCodes"/> (SDD-INV-001 §4, §6.4). Asserts the codes exist as
/// SCREAMING_SNAKE_CASE constants matching their own name, including the deferred SDD-FIN-004 closed-period
/// seam code that the always-open guard never returns but whose code and stub must exist.
/// </summary>
[TestFixture]
[Category("SDD-INV-001")]
public sealed class InvoiceErrorCodesTests
{
    /// <summary>The closed-period code exists for the deferred SDD-FIN-004 seam (§2.2, §2.13, §6.4).</summary>
    [Test]
    public void InvoiceErrorCodes_DefinesPeriodClosed_ForDeferredFin004Seam()
    {
        // Arrange & Act & Assert
        Assert.That(InvoiceErrorCodes.INVOICE_PERIOD_CLOSED, Is.EqualTo("INVOICE_PERIOD_CLOSED"));
    }

    /// <summary>The totals-mismatch code exists for the cross-field reconciliation rule (§2.8, §3.2, §6.4).</summary>
    [Test]
    public void InvoiceErrorCodes_DefinesTotalsMismatch()
    {
        // Arrange & Act & Assert
        Assert.That(InvoiceErrorCodes.INVOICE_TOTALS_MISMATCH, Is.EqualTo("INVOICE_TOTALS_MISMATCH"));
    }

    /// <summary>The state-conflict codes exist and are distinct (§4, §6.4).</summary>
    [Test]
    public void InvoiceErrorCodes_DefinesStateConflictCodes()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.That(InvoiceErrorCodes.INVOICE_NOT_DRAFT, Is.EqualTo("INVOICE_NOT_DRAFT"));
            Assert.That(InvoiceErrorCodes.INVOICE_NOT_CONFIRMED, Is.EqualTo("INVOICE_NOT_CONFIRMED"));
            Assert.That(InvoiceErrorCodes.INVOICE_POSTED_IMMUTABLE, Is.EqualTo("INVOICE_POSTED_IMMUTABLE"));
            Assert.That(
                InvoiceErrorCodes.INVALID_INVOICE_STATE_TRANSITION,
                Is.EqualTo("INVALID_INVOICE_STATE_TRANSITION"));
            Assert.That(
                InvoiceErrorCodes.INVOICE_DUPLICATE_DOCUMENT_NUMBER,
                Is.EqualTo("INVOICE_DUPLICATE_DOCUMENT_NUMBER"));
        });
    }
}
