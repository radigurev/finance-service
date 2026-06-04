using Finance.Country.Abstractions;
using Finance.Country.BG;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Finance.Country.BG.Tests;

/// <summary>
/// Unit tests for <see cref="BulgariaStrategy"/> (SDD-CTRY-001 §6.1–6.3): the country identity
/// (<c>BG</c>/<c>BGN</c>, uppercase and stable), the default posting-rule template contract (non-empty,
/// deterministic, unique rule keys, all tagged <c>BG</c>, every template structurally balanceable, and the
/// three sample НСС rules debit/credit/amount-source shape), the immutability of the template DTOs, the
/// <see cref="PostingAmountSource"/> enum surface, and the single-binding registration with no factory.
/// Every test is a fast in-memory <c>[Unit]</c> test with no infrastructure.
/// </summary>
[TestFixture]
[Category("SDD-CTRY-001")]
public sealed class BulgariaStrategyTests
{
    private const string SaleInvoiceKey = "SALE_INVOICE";
    private const string PurchaseInvoiceKey = "PURCHASE_INVOICE";
    private const string CustomerPaymentKey = "CUSTOMER_PAYMENT";

    private BulgariaStrategy _sut = null!;

    /// <summary>Creates a fresh strategy before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _sut = new BulgariaStrategy();
    }

    [Test]
    public void BulgariaStrategy_CountryCode_IsBG()
    {
        // Arrange — strategy created in SetUp.

        // Act
        string countryCode = _sut.CountryCode;

        // Assert
        Assert.That(countryCode, Is.EqualTo("BG"));
    }

    [Test]
    public void BulgariaStrategy_BaseCurrencyCode_IsBGN()
    {
        // Arrange — strategy created in SetUp.

        // Act
        string baseCurrencyCode = _sut.BaseCurrencyCode;

        // Assert
        Assert.That(baseCurrencyCode, Is.EqualTo("BGN"));
    }

    [Test]
    public void BulgariaStrategy_IdentityValues_AreUppercaseAndStable()
    {
        // Arrange
        string firstCountry = _sut.CountryCode;
        string firstCurrency = _sut.BaseCurrencyCode;

        // Act
        string secondCountry = _sut.CountryCode;
        string secondCurrency = _sut.BaseCurrencyCode;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(firstCountry, Is.Not.Null.And.Not.Empty);
            Assert.That(firstCurrency, Is.Not.Null.And.Not.Empty);
            Assert.That(firstCountry, Is.EqualTo(firstCountry.ToUpperInvariant()));
            Assert.That(firstCurrency, Is.EqualTo(firstCurrency.ToUpperInvariant()));
            Assert.That(secondCountry, Is.EqualTo(firstCountry));
            Assert.That(secondCurrency, Is.EqualTo(firstCurrency));
        });
    }

    [Test]
    public void BulgariaStrategy_GetDefaultPostingRules_ReturnsNonEmptyReadOnlyList()
    {
        // Arrange — strategy created in SetUp.

        // Act
        IReadOnlyList<PostingRuleTemplate> rules = _sut.GetDefaultPostingRules();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(rules, Is.Not.Null);
            Assert.That(rules, Is.Not.Empty);
        });
    }

    [Test]
    public void BulgariaStrategy_GetDefaultPostingRules_IsDeterministic()
    {
        // Arrange
        IReadOnlyList<PostingRuleTemplate> first = _sut.GetDefaultPostingRules();

        // Act
        IReadOnlyList<PostingRuleTemplate> second = _sut.GetDefaultPostingRules();

        // Assert
        List<string> firstKeys = first.Select(rule => rule.RuleKey).ToList();
        List<string> secondKeys = second.Select(rule => rule.RuleKey).ToList();
        Assert.That(secondKeys, Is.EqualTo(firstKeys));
    }

    [Test]
    public void BulgariaStrategy_DefaultRules_HaveUniqueRuleKeys()
    {
        // Arrange
        IReadOnlyList<PostingRuleTemplate> rules = _sut.GetDefaultPostingRules();

        // Act
        int distinctKeyCount = rules.Select(rule => rule.RuleKey).Distinct(StringComparer.Ordinal).Count();

        // Assert
        Assert.That(distinctKeyCount, Is.EqualTo(rules.Count));
    }

    [Test]
    public void BulgariaStrategy_DefaultRules_AllTaggedBG()
    {
        // Arrange
        IReadOnlyList<PostingRuleTemplate> rules = _sut.GetDefaultPostingRules();

        // Act & Assert
        Assert.That(rules, Has.All.Property(nameof(PostingRuleTemplate.CountryCode)).EqualTo("BG"));
    }

    [Test]
    public void BulgariaStrategy_EveryTemplate_HasDebitAndCreditLine()
    {
        // Arrange
        IReadOnlyList<PostingRuleTemplate> rules = _sut.GetDefaultPostingRules();

        // Act & Assert
        Assert.Multiple(() =>
        {
            foreach (PostingRuleTemplate rule in rules)
            {
                bool hasDebit = rule.Lines.Any(line => line.DebitOrCredit == PostingDebitOrCredit.Debit);
                bool hasCredit = rule.Lines.Any(line => line.DebitOrCredit == PostingDebitOrCredit.Credit);
                Assert.That(hasDebit, Is.True, $"Rule '{rule.RuleKey}' has no debit line.");
                Assert.That(hasCredit, Is.True, $"Rule '{rule.RuleKey}' has no credit line.");
            }
        });
    }

    [Test]
    public void BulgariaStrategy_DefaultRules_UseOnlyNetTaxGrossSources()
    {
        // Arrange
        IReadOnlyList<PostingRuleTemplate> rules = _sut.GetDefaultPostingRules();
        PostingAmountSource[] allowed =
            [PostingAmountSource.Net, PostingAmountSource.Tax, PostingAmountSource.Gross];

        // Act
        bool allWithinSet = rules
            .SelectMany(rule => rule.Lines)
            .All(line => allowed.Contains(line.AmountSource));

        // Assert
        Assert.That(allWithinSet, Is.True);
    }

    [Test]
    public void BulgariaStrategy_SaleInvoiceTemplate_DebitsReceivableCreditsRevenueAndVat()
    {
        // Arrange
        PostingRuleTemplate rule = SingleRule(SaleInvoiceKey);

        // Act
        PostingRuleLineTemplate receivable = LineFor(rule, "411");
        PostingRuleLineTemplate revenue = LineFor(rule, "701");
        PostingRuleLineTemplate vat = LineFor(rule, "4532");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(receivable.DebitOrCredit, Is.EqualTo(PostingDebitOrCredit.Debit));
            Assert.That(receivable.AmountSource, Is.EqualTo(PostingAmountSource.Gross));
            Assert.That(revenue.DebitOrCredit, Is.EqualTo(PostingDebitOrCredit.Credit));
            Assert.That(revenue.AmountSource, Is.EqualTo(PostingAmountSource.Net));
            Assert.That(vat.DebitOrCredit, Is.EqualTo(PostingDebitOrCredit.Credit));
            Assert.That(vat.AmountSource, Is.EqualTo(PostingAmountSource.Tax));
        });
    }

    [Test]
    public void BulgariaStrategy_PurchaseInvoiceTemplate_DebitsExpenseAndInputVatCreditsPayable()
    {
        // Arrange
        PostingRuleTemplate rule = SingleRule(PurchaseInvoiceKey);

        // Act
        PostingRuleLineTemplate expense = LineFor(rule, "601");
        PostingRuleLineTemplate inputVat = LineFor(rule, "4531");
        PostingRuleLineTemplate payable = LineFor(rule, "401");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(expense.DebitOrCredit, Is.EqualTo(PostingDebitOrCredit.Debit));
            Assert.That(expense.AmountSource, Is.EqualTo(PostingAmountSource.Net));
            Assert.That(inputVat.DebitOrCredit, Is.EqualTo(PostingDebitOrCredit.Debit));
            Assert.That(inputVat.AmountSource, Is.EqualTo(PostingAmountSource.Tax));
            Assert.That(payable.DebitOrCredit, Is.EqualTo(PostingDebitOrCredit.Credit));
            Assert.That(payable.AmountSource, Is.EqualTo(PostingAmountSource.Gross));
        });
    }

    [Test]
    public void BulgariaStrategy_CustomerPaymentTemplate_DebitsCashCreditsReceivable()
    {
        // Arrange
        PostingRuleTemplate rule = SingleRule(CustomerPaymentKey);

        // Act
        PostingRuleLineTemplate cash = LineFor(rule, "503");
        PostingRuleLineTemplate receivable = LineFor(rule, "411");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(cash.DebitOrCredit, Is.EqualTo(PostingDebitOrCredit.Debit));
            Assert.That(cash.AmountSource, Is.EqualTo(PostingAmountSource.Gross));
            Assert.That(receivable.DebitOrCredit, Is.EqualTo(PostingDebitOrCredit.Credit));
            Assert.That(receivable.AmountSource, Is.EqualTo(PostingAmountSource.Gross));
        });
    }

    [Test]
    public void PostingRuleTemplate_AndLineTemplate_AreImmutableRecords_NoBehavior()
    {
        // Arrange
        Type templateType = typeof(PostingRuleTemplate);
        Type lineType = typeof(PostingRuleLineTemplate);
        PostingRuleLineTemplate firstLine =
            Line("411", PostingDebitOrCredit.Debit, PostingAmountSource.Gross);
        PostingRuleLineTemplate secondLine =
            Line("411", PostingDebitOrCredit.Debit, PostingAmountSource.Gross);

        // Act
        bool templatePropsInitOnly = templateType.GetProperties()
            .All(property => property.SetMethod is null || IsInitOnly(property));
        bool linePropsInitOnly = lineType.GetProperties()
            .All(property => property.SetMethod is null || IsInitOnly(property));
        bool linesEqualByValue = firstLine == secondLine;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(templateType.IsSealed, Is.True);
            Assert.That(lineType.IsSealed, Is.True);
            Assert.That(templatePropsInitOnly, Is.True);
            Assert.That(linePropsInitOnly, Is.True);
            Assert.That(linesEqualByValue, Is.True);
            Assert.That(NoHandWrittenDomainMethods(templateType), Is.True);
            Assert.That(NoHandWrittenDomainMethods(lineType), Is.True);
        });
    }

    [Test]
    public void PostingAmountSource_DefinesNetTaxGross()
    {
        // Arrange & Act
        PostingAmountSource[] values = Enum.GetValues<PostingAmountSource>();

        // Assert
        Assert.That(
            values,
            Is.EquivalentTo(new[]
            {
                PostingAmountSource.Net,
                PostingAmountSource.Tax,
                PostingAmountSource.Gross
            }));
    }

    [Test]
    public void CountryStrategy_RegisteredAsSingleScopedBinding_NoFactory()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddScoped<ICountryStrategy, BulgariaStrategy>();

        // Act
        ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        ICountryStrategy resolved = scope.ServiceProvider.GetRequiredService<ICountryStrategy>();
        int registrationCount = services.Count(descriptor => descriptor.ServiceType == typeof(ICountryStrategy));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.InstanceOf<BulgariaStrategy>());
            Assert.That(registrationCount, Is.EqualTo(1));
        });
    }

    private PostingRuleTemplate SingleRule(string ruleKey) =>
        _sut.GetDefaultPostingRules().Single(rule => rule.RuleKey == ruleKey);

    private static PostingRuleLineTemplate LineFor(PostingRuleTemplate rule, string accountSelector) =>
        rule.Lines.Single(line => line.AccountSelector == accountSelector);

    private static PostingRuleLineTemplate Line(
        string accountSelector,
        PostingDebitOrCredit debitOrCredit,
        PostingAmountSource amountSource) => new()
    {
        AccountSelector = accountSelector,
        DebitOrCredit = debitOrCredit,
        AmountSource = amountSource
    };

    private static bool IsInitOnly(System.Reflection.PropertyInfo property) =>
        property.SetMethod!.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    private static bool NoHandWrittenDomainMethods(Type type)
    {
        string[] recordGenerated =
            ["Equals", "GetHashCode", "ToString", "PrintMembers", "Deconstruct", "<Clone>$"];
        return type
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .All(method => recordGenerated.Contains(method.Name));
    }
}
