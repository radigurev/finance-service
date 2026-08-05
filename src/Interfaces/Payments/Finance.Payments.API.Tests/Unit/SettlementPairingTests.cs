using Finance.Common.Enums;
using Finance.Common.Settlement;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit;

/// <summary>
/// Unit tests for the shared <see cref="SettlementPairing"/> table (SDD-PAY-002 §2.5 rule 10, §6.5). It is the ONE
/// place the two-valued <c>PaymentDocumentType</c> and four-valued <c>InvoiceDocumentType</c> are related, and the
/// §2.3 projection-admission predicate is DERIVED from the pairs rather than being a second literal list — which is
/// what makes admission and allocation incapable of drifting.
/// </summary>
[TestFixture]
[Category("SDD-PAY-002")]
public sealed class SettlementPairingTests
{
    [Test]
    public void SettlementPairing_AllowsExactlyTheDocumentedPairs_CustomerReceiptAndSupplierPayment()
    {
        // Arrange
        IReadOnlySet<InvoiceDocumentType> receiptPairs =
            SettlementPairing.AllocatableInvoiceTypesFor(PaymentDocumentType.CustomerReceipt);
        IReadOnlySet<InvoiceDocumentType> supplierPairs =
            SettlementPairing.AllocatableInvoiceTypesFor(PaymentDocumentType.SupplierPayment);

        // Act
        IReadOnlyList<(PaymentDocumentType Payment, InvoiceDocumentType Invoice)> allowed =
        [
            .. Enum.GetValues<PaymentDocumentType>()
                .SelectMany(payment => Enum.GetValues<InvoiceDocumentType>()
                    .Select(invoice => (Payment: payment, Invoice: invoice)))
                .Where(pair => SettlementPairing.CanSettle(pair.Payment, pair.Invoice))
        ];

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(receiptPairs, Is.EquivalentTo(new[]
            {
                InvoiceDocumentType.SaleInvoice,
                InvoiceDocumentType.DebitNote
            }));
            Assert.That(supplierPairs, Is.EquivalentTo(new[] { InvoiceDocumentType.PurchaseInvoice }));
            Assert.That(allowed, Has.Count.EqualTo(3), "exactly three pairs are documented");
            Assert.That(
                SettlementPairing.CanSettle(
                    PaymentDocumentType.SupplierPayment, InvoiceDocumentType.CreditNote),
                Is.False,
                "a supplier payment against a customer credit note moves a DIFFERENT control account");
            Assert.That(
                SettlementPairing.CanSettle(
                    PaymentDocumentType.CustomerReceipt, InvoiceDocumentType.PurchaseInvoice),
                Is.False);
        });
    }

    [Test]
    public void SettlementPairing_IsSettleableInvoiceType_IsDerivedFromCanSettle_ExcludesOnlyCreditNote()
    {
        // Arrange
        IReadOnlyList<PaymentDocumentType> paymentTypes = [.. Enum.GetValues<PaymentDocumentType>()];

        // Act
        IReadOnlyList<InvoiceDocumentType> settleable =
            [.. Enum.GetValues<InvoiceDocumentType>().Where(SettlementPairing.IsSettleableInvoiceType)];
        IReadOnlyList<InvoiceDocumentType> derived =
        [
            .. Enum.GetValues<InvoiceDocumentType>()
                .Where(invoice => paymentTypes.Any(payment => SettlementPairing.CanSettle(payment, invoice)))
        ];

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(settleable, Is.EqualTo(derived), "the admission predicate is derived, not a second list");
            Assert.That(settleable, Is.EquivalentTo(new[]
            {
                InvoiceDocumentType.SaleInvoice,
                InvoiceDocumentType.PurchaseInvoice,
                InvoiceDocumentType.DebitNote
            }));
            Assert.That(
                SettlementPairing.IsSettleableInvoiceType(InvoiceDocumentType.CreditNote),
                Is.False,
                "v1 excludes exactly one type");
        });
    }

    [Test]
    public void SettlementPairing_UnrecognizedPaymentDocumentType_YieldsEmptySetRatherThanThrowing()
    {
        // Arrange
        PaymentDocumentType unknown = (PaymentDocumentType)99;

        // Act
        IReadOnlySet<InvoiceDocumentType> pairs = SettlementPairing.AllocatableInvoiceTypesFor(unknown);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(pairs, Is.Empty);
            Assert.That(SettlementPairing.CanSettle(unknown, InvoiceDocumentType.SaleInvoice), Is.False);
        });
    }
}
