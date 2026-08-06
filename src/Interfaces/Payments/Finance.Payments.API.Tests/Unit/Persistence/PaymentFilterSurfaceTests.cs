using System.Reflection;
using Finance.GenericFiltering.Attributes;
using Finance.Payments.DBModel.Models;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Persistence;

/// <summary>
/// Unit tests pinning the CLOSED opt-in filter surface of <see cref="Payment"/> (SDD-PAY-001 §2.11, SDD-INFRA-005).
/// The list endpoint exposes only the properties that carry <c>[Filterable]</c>/<c>[Sortable]</c>, so a stray
/// attribute silently widens the public query surface and a missing one silently breaks a documented filter —
/// neither shows up in a behavioural test. These read attribute metadata only; no database is opened.
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
[Category("SDD-INFRA-005")]
public sealed class PaymentFilterSurfaceTests
{
    private static readonly string[] ExpectedFilterable =
    [
        nameof(Payment.DocumentNumber),
        nameof(Payment.DocumentType),
        nameof(Payment.Direction),
        nameof(Payment.Method),
        nameof(Payment.Status),
        nameof(Payment.CounterpartyId),
        nameof(Payment.CurrencyCode),
        nameof(Payment.PaymentDate),
        nameof(Payment.SettlementAccountId),
        nameof(Payment.Amount),
        nameof(Payment.CreatedAt)
    ];

    private static readonly string[] ExpectedSortable =
    [
        nameof(Payment.DocumentNumber),
        nameof(Payment.DocumentType),
        nameof(Payment.Direction),
        nameof(Payment.Method),
        nameof(Payment.Status),
        nameof(Payment.CurrencyCode),
        nameof(Payment.PaymentDate),
        nameof(Payment.SettlementAccountId),
        nameof(Payment.Amount),
        nameof(Payment.CreatedAt)
    ];

    [Test]
    public void Search_ExposesOnlyTheEnumeratedFilterableAndSortableProperties()
    {
        // Arrange
        PropertyInfo[] properties = typeof(Payment).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // Act
        IReadOnlyList<string> filterable = NamesDecoratedWith<FilterableAttribute>(properties);
        IReadOnlyList<string> sortable = NamesDecoratedWith<SortableAttribute>(properties);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(filterable, Is.EquivalentTo(ExpectedFilterable));
            Assert.That(sortable, Is.EquivalentTo(ExpectedSortable));
        });
    }

    [Test]
    public void Search_CounterpartyId_IsFilterableButNotSortable()
    {
        // Arrange
        PropertyInfo counterpartyId = typeof(Payment).GetProperty(nameof(Payment.CounterpartyId))!;

        // Act
        bool isFilterable = counterpartyId.IsDefined(typeof(FilterableAttribute), inherit: true);
        bool isSortable = counterpartyId.IsDefined(typeof(SortableAttribute), inherit: true);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(isFilterable, Is.True);
            Assert.That(isSortable, Is.False, "sorting a page by an opaque Warehouse GUID has no user meaning");
        });
    }

    [Test]
    public void Search_DocumentNumber_IsTheOnlySearchableProperty()
    {
        // Arrange
        PropertyInfo[] properties = typeof(Payment).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // Act
        IReadOnlyList<string> searchable = NamesDecoratedWith<SearchableAttribute>(properties);

        // Assert
        Assert.That(searchable, Is.EquivalentTo(new[] { nameof(Payment.DocumentNumber) }));
    }

    [Test]
    public void Search_SettlementBookkeepingColumns_CarryNeitherAttribute()
    {
        // Arrange
        PropertyInfo[] properties = typeof(Payment).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        IReadOnlyList<string> opted =
        [
            .. NamesDecoratedWith<FilterableAttribute>(properties),
            .. NamesDecoratedWith<SortableAttribute>(properties)
        ];

        // Act
        IReadOnlyList<string> leaked =
        [
            .. new[]
            {
                nameof(Payment.AllocatedAmount),
                nameof(Payment.UnallocatedAmount),
                nameof(Payment.BaseAmount),
                nameof(Payment.ExchangeRate),
                nameof(Payment.BaseCurrencyCode),
                nameof(Payment.BankReference),
                nameof(Payment.JournalEntryId),
                nameof(Payment.CancellationReason),
                nameof(Payment.CorrelationId),
                nameof(Payment.CreatedBy),
                nameof(Payment.ConfirmedBy)
            }.Where(opted.Contains)
        ];

        // Assert
        Assert.That(leaked, Is.Empty, "the §2.11 list is CLOSED — no column becomes queryable by accident");
    }

    /// <summary>Projects the property names carrying the requested opt-in attribute.</summary>
    /// <typeparam name="TAttribute">The opt-in attribute.</typeparam>
    /// <param name="properties">The candidate properties.</param>
    /// <returns>The decorated property names.</returns>
    private static IReadOnlyList<string> NamesDecoratedWith<TAttribute>(PropertyInfo[] properties)
        where TAttribute : Attribute =>
        [.. properties.Where(property => property.IsDefined(typeof(TAttribute), inherit: true))
            .Select(property => property.Name)];
}
