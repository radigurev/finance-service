using System.Globalization;
using Finance.Common.Enums;
using Finance.Country.Abstractions;
using NUnit.Framework;

namespace Finance.Country.BG.Tests;

/// <summary>
/// Unit tests for the posting-rule templates and the payment-typed document numbering
/// <see cref="BulgariaStrategy"/> grew for the payment lifecycle (SDD-PAY-001 §2.13) and for the credit/debit
/// notes SDD-INV-001 §7 left unseeded.
/// <para>Every assertion pins the account SELECTOR, the Debit/Credit SIDE, and the
/// <see cref="PostingAmountSource"/> of EVERY line — never merely that a rule balances. Asserting balance alone
/// is insufficient because <c>PostingEngine.CheckBalanced</c> compares only total base debits to total base
/// credits, so a template whose sides were copied instead of flipped is exactly as "balanced" as one whose sides
/// were flipped, and <c>PostingRuleSeeder</c> never overwrites an existing rule key — a wrong-sided
/// <c>CREDIT_NOTE</c> would make every credit note INCREASE receivables, revenue, and output VAT permanently
/// (SDD-PAY-001 §2.13).</para>
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
[Category("SDD-CTRY-001")]
public sealed class BulgariaStrategyPostingRuleTemplateTests
{
    private const string SaleInvoiceKey = "SALE_INVOICE";
    private const string CustomerPaymentKey = "CUSTOMER_PAYMENT";
    private const string CreditNoteKey = "CREDIT_NOTE";
    private const string DebitNoteKey = "DEBIT_NOTE";
    private const string PaymentCustomerReceiptKey = "PAYMENT_CUSTOMER_RECEIPT";
    private const string PaymentSupplierPaymentKey = "PAYMENT_SUPPLIER_PAYMENT";

    private const string CustomersAccount = "411";
    private const string SuppliersAccount = "401";
    private const string RevenueAccount = "701";
    private const string OutputVatAccount = "4532";
    private const string CashAccount = "503";

    private BulgariaStrategy _sut = null!;

    /// <summary>Creates a fresh strategy before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _sut = new BulgariaStrategy();
    }

    /// <summary>
    /// The two payment templates the confirm→post handshake depends on are seeded with the exact selectors and
    /// sides of SDD-PAY-001 §2.13: a receipt debits cash and credits customers; a supplier payment debits
    /// suppliers and credits cash — both on the Gross amount source.
    /// </summary>
    [Test]
    public void BulgariaStrategy_SeedsPaymentCustomerReceiptAndPaymentSupplierPaymentRules_WithAssertedSelectorsAndSides()
    {
        // Arrange
        PostingRuleTemplate receipt = SingleRule(PaymentCustomerReceiptKey);
        PostingRuleTemplate supplierPayment = SingleRule(PaymentSupplierPaymentKey);

        // Act
        PostingRuleLineTemplate receiptCash = LineFor(receipt, CashAccount);
        PostingRuleLineTemplate receiptCustomers = LineFor(receipt, CustomersAccount);
        PostingRuleLineTemplate paymentSuppliers = LineFor(supplierPayment, SuppliersAccount);
        PostingRuleLineTemplate paymentCash = LineFor(supplierPayment, CashAccount);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(receipt.Lines, Has.Count.EqualTo(2));
            Assert.That(receipt.CountryCode, Is.EqualTo("BG"));
            Assert.That(receiptCash.DebitOrCredit, Is.EqualTo(PostingDebitOrCredit.Debit));
            Assert.That(receiptCash.AmountSource, Is.EqualTo(PostingAmountSource.Gross));
            Assert.That(receiptCustomers.DebitOrCredit, Is.EqualTo(PostingDebitOrCredit.Credit));
            Assert.That(receiptCustomers.AmountSource, Is.EqualTo(PostingAmountSource.Gross));

            Assert.That(supplierPayment.Lines, Has.Count.EqualTo(2));
            Assert.That(supplierPayment.CountryCode, Is.EqualTo("BG"));
            Assert.That(paymentSuppliers.DebitOrCredit, Is.EqualTo(PostingDebitOrCredit.Debit));
            Assert.That(paymentSuppliers.AmountSource, Is.EqualTo(PostingAmountSource.Gross));
            Assert.That(paymentCash.DebitOrCredit, Is.EqualTo(PostingDebitOrCredit.Credit));
            Assert.That(paymentCash.AmountSource, Is.EqualTo(PostingAmountSource.Gross));
        });
    }

    /// <summary>
    /// The superseded sample <c>CUSTOMER_PAYMENT</c> template is RETAINED alongside the new
    /// <c>PAYMENT_CUSTOMER_RECEIPT</c> rather than reused or renamed — renaming a live rule key would orphan
    /// administrator edits, and the seeder never overwrites (SDD-PAY-001 §2.13).
    /// </summary>
    [Test]
    public void BulgariaStrategy_RetainsSupersededCustomerPaymentRule_AlongsideNewPaymentCustomerReceiptRule()
    {
        // Arrange
        IReadOnlyList<PostingRuleTemplate> rules = _sut.GetDefaultPostingRules();

        // Act
        IEnumerable<string> keys = rules.Select(rule => rule.RuleKey);

        // Assert
        Assert.That(keys, Is.SupersetOf(new[] { CustomerPaymentKey, PaymentCustomerReceiptKey }));
    }

    /// <summary>
    /// The credit note MIRRORS the sale invoice with the sides REVERSED (SDD-PAY-001 §2.13): credit customers
    /// (gross), debit sales revenue (net), debit output VAT (tax).
    /// </summary>
    [Test]
    [Category("SDD-INV-001")]
    public void BulgariaStrategy_SeedsCreditNoteRule_WithSidesMirroringSaleInvoice()
    {
        // Arrange
        PostingRuleTemplate rule = SingleRule(CreditNoteKey);

        // Act
        PostingRuleLineTemplate customers = LineFor(rule, CustomersAccount);
        PostingRuleLineTemplate revenue = LineFor(rule, RevenueAccount);
        PostingRuleLineTemplate outputVat = LineFor(rule, OutputVatAccount);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(rule.Lines, Has.Count.EqualTo(3));
            Assert.That(rule.CountryCode, Is.EqualTo("BG"));
            Assert.That(customers.DebitOrCredit, Is.EqualTo(PostingDebitOrCredit.Credit));
            Assert.That(customers.AmountSource, Is.EqualTo(PostingAmountSource.Gross));
            Assert.That(revenue.DebitOrCredit, Is.EqualTo(PostingDebitOrCredit.Debit));
            Assert.That(revenue.AmountSource, Is.EqualTo(PostingAmountSource.Net));
            Assert.That(outputVat.DebitOrCredit, Is.EqualTo(PostingDebitOrCredit.Debit));
            Assert.That(outputVat.AmountSource, Is.EqualTo(PostingAmountSource.Tax));
        });
    }

    /// <summary>
    /// The anti-copy-paste assertion: every credit-note line takes the OPPOSITE side to the sale-invoice line
    /// for the same account selector, while keeping the same amount source (SDD-PAY-001 §2.13).
    /// </summary>
    [Test]
    [Category("SDD-INV-001")]
    public void BulgariaStrategy_CreditNoteRule_TakesOppositeSideToSaleInvoice_ForEveryAccountSelector()
    {
        // Arrange
        PostingRuleTemplate saleInvoice = SingleRule(SaleInvoiceKey);
        PostingRuleTemplate creditNote = SingleRule(CreditNoteKey);

        // Act & Assert
        Assert.Multiple(() =>
        {
            foreach (PostingRuleLineTemplate saleLine in saleInvoice.Lines)
            {
                PostingRuleLineTemplate creditLine = LineFor(creditNote, saleLine.AccountSelector);
                Assert.That(
                    creditLine.DebitOrCredit,
                    Is.Not.EqualTo(saleLine.DebitOrCredit),
                    $"Credit-note line '{saleLine.AccountSelector}' repeats the sale-invoice side.");
                Assert.That(creditLine.AmountSource, Is.EqualTo(saleLine.AmountSource));
            }
        });
    }

    /// <summary>
    /// The debit note REPEATS the sale invoice's sides (SDD-PAY-001 §2.13): debit customers (gross), credit
    /// sales revenue (net), credit output VAT (tax).
    /// </summary>
    [Test]
    [Category("SDD-INV-001")]
    public void BulgariaStrategy_SeedsDebitNoteRule_WithSidesRepeatingSaleInvoice()
    {
        // Arrange
        PostingRuleTemplate rule = SingleRule(DebitNoteKey);

        // Act
        PostingRuleLineTemplate customers = LineFor(rule, CustomersAccount);
        PostingRuleLineTemplate revenue = LineFor(rule, RevenueAccount);
        PostingRuleLineTemplate outputVat = LineFor(rule, OutputVatAccount);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(rule.Lines, Has.Count.EqualTo(3));
            Assert.That(rule.CountryCode, Is.EqualTo("BG"));
            Assert.That(customers.DebitOrCredit, Is.EqualTo(PostingDebitOrCredit.Debit));
            Assert.That(customers.AmountSource, Is.EqualTo(PostingAmountSource.Gross));
            Assert.That(revenue.DebitOrCredit, Is.EqualTo(PostingDebitOrCredit.Credit));
            Assert.That(revenue.AmountSource, Is.EqualTo(PostingAmountSource.Net));
            Assert.That(outputVat.DebitOrCredit, Is.EqualTo(PostingDebitOrCredit.Credit));
            Assert.That(outputVat.AmountSource, Is.EqualTo(PostingAmountSource.Tax));
        });
    }

    /// <summary>
    /// The debit note repeats the sale invoice line for line — same selector, same side, same amount source
    /// (SDD-PAY-001 §2.13).
    /// </summary>
    [Test]
    [Category("SDD-INV-001")]
    public void BulgariaStrategy_DebitNoteRule_RepeatsEverySaleInvoiceLine_SelectorSideAndAmountSource()
    {
        // Arrange
        PostingRuleTemplate saleInvoice = SingleRule(SaleInvoiceKey);
        PostingRuleTemplate debitNote = SingleRule(DebitNoteKey);

        // Act & Assert
        Assert.Multiple(() =>
        {
            foreach (PostingRuleLineTemplate saleLine in saleInvoice.Lines)
            {
                PostingRuleLineTemplate debitLine = LineFor(debitNote, saleLine.AccountSelector);
                Assert.That(debitLine.DebitOrCredit, Is.EqualTo(saleLine.DebitOrCredit));
                Assert.That(debitLine.AmountSource, Is.EqualTo(saleLine.AmountSource));
            }
        });
    }

    /// <summary>
    /// The payment-typed overload formats a receipt as <c>RCT-{yyyy}-{nnnnnn}</c> and a supplier payment as
    /// <c>PAY-{yyyy}-{nnnnnn}</c>, with the year pinned to the confirm clock (SDD-PAY-001 §2.4, §2.13).
    /// </summary>
    [TestCase(PaymentDocumentType.CustomerReceipt, "RCT")]
    [TestCase(PaymentDocumentType.SupplierPayment, "PAY")]
    public void BulgariaStrategy_GeneratesPaymentDocumentNumber_WithCountryPrefixAndPaddedCounter(
        PaymentDocumentType documentType,
        string expectedPrefix)
    {
        // Arrange
        long sequenceValue = 42;
        string year = DateTimeOffset.UtcNow.Year.ToString(CultureInfo.InvariantCulture);

        // Act
        string number = _sut.GenerateDocumentNumber(documentType, sequenceValue);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(number, Is.EqualTo($"{expectedPrefix}-{year}-000042"));
            Assert.That(number, Does.Match($"^{expectedPrefix}-[0-9]{{4}}-[0-9]{{6}}$"));
        });
    }

    /// <summary>
    /// The payment number pads the gapless counter to six digits and never truncates a wider counter
    /// (SDD-INFRA-003 §2.1 padding, asserted through SDD-PAY-001 §2.13).
    /// </summary>
    [TestCase(1L, "000001")]
    [TestCase(999999L, "999999")]
    [TestCase(1000000L, "1000000")]
    public void BulgariaStrategy_GeneratesPaymentDocumentNumber_PadsCounterToSixDigits(
        long sequenceValue,
        string expectedCounter)
    {
        // Arrange
        string year = DateTimeOffset.UtcNow.Year.ToString(CultureInfo.InvariantCulture);

        // Act
        string number = _sut.GenerateDocumentNumber(PaymentDocumentType.CustomerReceipt, sequenceValue);

        // Assert
        Assert.That(number, Is.EqualTo($"RCT-{year}-{expectedCounter}"));
    }

    private PostingRuleTemplate SingleRule(string ruleKey) =>
        _sut.GetDefaultPostingRules().Single(rule => rule.RuleKey == ruleKey);

    private static PostingRuleLineTemplate LineFor(PostingRuleTemplate rule, string accountSelector) =>
        rule.Lines.Single(line => line.AccountSelector == accountSelector);
}
