using Finance.Common.Enums;
using Finance.Infrastructure.Sequences;
using Finance.Payments.API.Services;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="PaymentDocumentTypeMap"/> (SDD-PAY-001 §2.3, §2.4, §2.13, §6.4): the single source of
/// the per-document-type discriminators — the ALREADY-EXISTING <c>RCT</c>/<c>PAY</c> sequence keys, the derived and
/// frozen direction, and the posting-rule key carried on <c>PaymentConfirmedEvent</c>.
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
public sealed class PaymentDocumentTypeMapTests
{
    [Test]
    public void PaymentDocumentTypeMap_MapsSequenceKeyDirectionAndPostingRuleKey_PerDocumentType()
    {
        // Arrange
        PaymentDocumentType receipt = PaymentDocumentType.CustomerReceipt;
        PaymentDocumentType supplierPayment = PaymentDocumentType.SupplierPayment;

        // Act
        string receiptSequenceKey = PaymentDocumentTypeMap.SequenceKeyFor(receipt);
        string supplierSequenceKey = PaymentDocumentTypeMap.SequenceKeyFor(supplierPayment);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(receiptSequenceKey, Is.EqualTo(SequenceKeys.Receipt));
            Assert.That(receiptSequenceKey, Is.EqualTo("RCT"));
            Assert.That(supplierSequenceKey, Is.EqualTo(SequenceKeys.Payment));
            Assert.That(supplierSequenceKey, Is.EqualTo("PAY"));
            Assert.That(PaymentDocumentTypeMap.DirectionFor(receipt), Is.EqualTo(PaymentDirection.AR));
            Assert.That(PaymentDocumentTypeMap.DirectionFor(supplierPayment), Is.EqualTo(PaymentDirection.AP));
            Assert.That(
                PaymentDocumentTypeMap.PostingRuleKeyFor(receipt),
                Is.EqualTo("PAYMENT_CUSTOMER_RECEIPT"));
            Assert.That(
                PaymentDocumentTypeMap.PostingRuleKeyFor(supplierPayment),
                Is.EqualTo("PAYMENT_SUPPLIER_PAYMENT"));
        });
    }

    [Test]
    public void PaymentDocumentTypeMap_UnknownDocumentType_ThrowsArgumentOutOfRange()
    {
        // Arrange
        PaymentDocumentType unknown = (PaymentDocumentType)99;

        // Act & Assert
        Assert.Multiple(() =>
        {
            Assert.That(
                () => PaymentDocumentTypeMap.SequenceKeyFor(unknown),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => PaymentDocumentTypeMap.DirectionFor(unknown),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => PaymentDocumentTypeMap.PostingRuleKeyFor(unknown),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void PaymentDirection_MirrorsInvoiceDirection_ValueForValue()
    {
        // Arrange
        int paymentAp = (int)PaymentDirection.AP;
        int paymentAr = (int)PaymentDirection.AR;

        // Act
        int invoiceAp = (int)InvoiceDirection.AP;
        int invoiceAr = (int)InvoiceDirection.AR;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(paymentAp, Is.EqualTo(invoiceAp));
            Assert.That(paymentAr, Is.EqualTo(invoiceAr));
            Assert.That(Enum.GetNames<PaymentDirection>(), Is.EquivalentTo(Enum.GetNames<InvoiceDirection>()));
        });
    }
}
