using System.Text.Json;
using Finance.Common.ErrorCodes;
using Finance.GenericFiltering.Exceptions;
using Finance.GenericFiltering.Models;
using Finance.GenericFiltering.Tests.TestEntities;
using NUnit.Framework;

namespace Finance.GenericFiltering.Tests;

/// <summary>
/// Unit tests for <c>QueryableFilterExtensions.ApplyFilter</c> covering operator translation,
/// sorting, paging, the deterministic final sort, search, and validation (SDD-INFRA-005).
/// LINQ-to-Objects (an in-memory <c>List&lt;T&gt;.AsQueryable()</c>) is used as the queryable
/// provider since no SQL Server is available in this environment.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-005")]
public sealed class QueryableFilterExtensionsTests
{
    private static IQueryable<AccountRow> Seed()
    {
        return new List<AccountRow>
        {
            Row(1, "100", "Cash", AccountKind.Asset, true, 100m, "2026-01-01T00:00:00+00:00", null),
            Row(2, "200", "Loans", AccountKind.Liability, true, 250m, "2026-03-01T00:00:00+00:00", 1),
            Row(3, "300", "Capital", AccountKind.Equity, false, 500m, "2026-06-01T00:00:00+00:00", null),
            Row(4, "110", "Bank ДДС", AccountKind.Asset, true, 750m, "2026-09-01T00:00:00+00:00", 1)
        }.AsQueryable();
    }

    private static AccountRow Row(
        int id,
        string code,
        string name,
        AccountKind kind,
        bool active,
        decimal balance,
        string created,
        int? parentId)
    {
        return new AccountRow
        {
            Id = id,
            Code = code,
            Name = name,
            Kind = kind,
            IsActive = active,
            Balance = balance,
            CreatedAt = DateTimeOffset.Parse(created),
            ParentId = parentId
        };
    }

    /// <summary>The <c>eq</c> operator matches an enum value by name.</summary>
    [Test]
    public void ApplyFilter_TranslatesEqOperatorCorrectly()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Kind", Operator = "eq", Value = "Asset" }]
        };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 1, 4 }));
    }

    /// <summary>The <c>neq</c> operator excludes matching rows.</summary>
    [Test]
    public void ApplyFilter_TranslatesNeqOperatorCorrectly()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Kind", Operator = "neq", Value = "Asset" }]
        };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 2, 3 }));
    }

    /// <summary>The <c>gt</c> operator selects rows strictly greater than the bound.</summary>
    [Test]
    public void ApplyFilter_TranslatesGtOperatorCorrectly()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Balance", Operator = "gt", Value = "250" }]
        };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 3, 4 }));
    }

    /// <summary>The <c>gte</c> operator includes the boundary value.</summary>
    [Test]
    public void ApplyFilter_TranslatesGteOperatorCorrectly()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Balance", Operator = "gte", Value = "250" }]
        };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 2, 3, 4 }));
    }

    /// <summary>The <c>lt</c> operator selects rows strictly less than the bound.</summary>
    [Test]
    public void ApplyFilter_TranslatesLtOperatorCorrectly()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Balance", Operator = "lt", Value = "250" }]
        };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 1 }));
    }

    /// <summary>The <c>lte</c> operator includes the boundary value.</summary>
    [Test]
    public void ApplyFilter_TranslatesLteOperatorCorrectly()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Balance", Operator = "lte", Value = "250" }]
        };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 1, 2 }));
    }

    /// <summary>The <c>contains</c> operator performs a substring match on a string property.</summary>
    [Test]
    public void ApplyFilter_TranslatesContainsOperatorCorrectly()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Name", Operator = "contains", Value = "an" }]
        };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 2, 4 }));
    }

    /// <summary>The <c>startsWith</c> operator performs a prefix match on a string property.</summary>
    [Test]
    public void ApplyFilter_TranslatesStartsWithOperatorCorrectly()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Code", Operator = "startsWith", Value = "1" }]
        };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 1, 4 }));
    }

    /// <summary>The <c>endsWith</c> operator performs a suffix match on a string property.</summary>
    [Test]
    public void ApplyFilter_TranslatesEndsWithOperatorCorrectly()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Code", Operator = "endsWith", Value = "00" }]
        };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    /// <summary>The <c>between</c> operator selects rows within an inclusive range.</summary>
    [Test]
    public void ApplyFilter_TranslatesBetweenOperatorCorrectly()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters =
            [
                new FilterCriterion
                {
                    Field = "CreatedAt",
                    Operator = "between",
                    Value = new[] { "2026-02-01T00:00:00+00:00", "2026-07-01T00:00:00+00:00" }
                }
            ]
        };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 2, 3 }));
    }

    /// <summary>The <c>in</c> operator selects rows whose value is in the supplied array.</summary>
    [Test]
    public void ApplyFilter_TranslatesInOperatorCorrectly()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters =
            [
                new FilterCriterion { Field = "Kind", Operator = "in", Value = new[] { "Asset", "Equity" } }
            ]
        };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 1, 3, 4 }));
    }

    /// <summary>The <c>isNull</c> operator selects rows whose nullable value type is null.</summary>
    [Test]
    public void ApplyFilter_TranslatesIsNullOperatorCorrectly()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "ParentId", Operator = "isNull", Value = null }]
        };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 1, 3 }));
    }

    /// <summary>The <c>isNotNull</c> operator selects rows whose nullable value type has a value.</summary>
    [Test]
    public void ApplyFilter_TranslatesIsNotNullOperatorCorrectly()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "ParentId", Operator = "isNotNull", Value = null }]
        };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 2, 4 }));
    }

    /// <summary>Multiple filter criteria combine with logical AND.</summary>
    [Test]
    public void ApplyFilter_CombinesMultipleCriteriaWithAnd()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters =
            [
                new FilterCriterion { Field = "Kind", Operator = "eq", Value = "Asset" },
                new FilterCriterion { Field = "Balance", Operator = "gt", Value = "200" }
            ]
        };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 4 }));
    }

    /// <summary>Client sort plus paging selects the correct ordered window.</summary>
    [Test]
    public void ApplyFilter_AppliesSortAndPaging()
    {
        // Arrange
        FilterRequest request = new()
        {
            Sort = [new SortCriterion { Field = "Balance", Direction = "desc" }],
            Page = 1,
            PageSize = 2
        };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 4, 3 }));
    }

    /// <summary>The deterministic <c>Id</c> tiebreaker stabilizes ordering when client sort ties.</summary>
    [Test]
    public void ApplyFilter_AlwaysAppendsPkAsFinalSort()
    {
        // Arrange
        IQueryable<AccountRow> data = new List<AccountRow>
        {
            Row(3, "X", "A", AccountKind.Asset, true, 100m, "2026-01-01T00:00:00+00:00", null),
            Row(1, "X", "B", AccountKind.Asset, true, 100m, "2026-01-01T00:00:00+00:00", null),
            Row(2, "X", "C", AccountKind.Asset, true, 100m, "2026-01-01T00:00:00+00:00", null)
        }.AsQueryable();

        FilterRequest request = new()
        {
            Sort = [new SortCriterion { Field = "Balance", Direction = "asc" }]
        };

        // Act
        List<AccountRow> result = data.ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    /// <summary>With no client sort, the deterministic <c>Id</c> tiebreaker still orders results.</summary>
    [Test]
    public void ApplyFilter_OrdersByIdWhenNoClientSortSupplied()
    {
        // Arrange
        IQueryable<AccountRow> data = new List<AccountRow>
        {
            Row(3, "C", "A", AccountKind.Asset, true, 100m, "2026-01-01T00:00:00+00:00", null),
            Row(1, "A", "B", AccountKind.Asset, true, 100m, "2026-01-01T00:00:00+00:00", null),
            Row(2, "B", "C", AccountKind.Asset, true, 100m, "2026-01-01T00:00:00+00:00", null)
        }.AsQueryable();

        FilterRequest request = new();

        // Act
        List<AccountRow> result = data.ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    /// <summary>When the entity has no <c>Id</c>, the final sort falls back to the first declared sortable.</summary>
    [Test]
    public void ApplyFilter_FallsBackToFirstSortableWhenNoIdProperty()
    {
        // Arrange
        IQueryable<RowWithoutId> data = new List<RowWithoutId>
        {
            new() { Code = "C", Rank = 1 },
            new() { Code = "A", Rank = 2 },
            new() { Code = "B", Rank = 3 }
        }.AsQueryable();

        FilterRequest request = new();

        // Act
        List<RowWithoutId> result = data.ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Code), Is.EqualTo(new[] { "A", "B", "C" }));
    }

    /// <summary>A filter on a property not marked <c>[Filterable]</c> is rejected.</summary>
    [Test]
    public void ApplyFilter_RejectsNonFilterableField()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Notes", Operator = "eq", Value = "x" }]
        };

        // Act
        FilterValidationException ex = Assert.Throws<FilterValidationException>(
            () => Seed().ApplyFilter(request).ToList())!;

        // Assert
        Assert.That(ex.ErrorCode, Is.EqualTo(FilterErrorCodes.INVALID_FILTER_FIELD));
    }

    /// <summary>A filter on a property absent from the entity is rejected as non-filterable.</summary>
    [Test]
    public void ApplyFilter_RejectsUnknownField()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "DoesNotExist", Operator = "eq", Value = "x" }]
        };

        // Act
        FilterValidationException ex = Assert.Throws<FilterValidationException>(
            () => Seed().ApplyFilter(request).ToList())!;

        // Assert
        Assert.That(ex.ErrorCode, Is.EqualTo(FilterErrorCodes.INVALID_FILTER_FIELD));
    }

    /// <summary>A sort on a property not marked <c>[Sortable]</c> is rejected.</summary>
    [Test]
    public void ApplyFilter_RejectsNonSortableField()
    {
        // Arrange
        FilterRequest request = new()
        {
            Sort = [new SortCriterion { Field = "Notes", Direction = "asc" }]
        };

        // Act
        FilterValidationException ex = Assert.Throws<FilterValidationException>(
            () => Seed().ApplyFilter(request).ToList())!;

        // Assert
        Assert.That(ex.ErrorCode, Is.EqualTo(FilterErrorCodes.INVALID_SORT_FIELD));
    }

    /// <summary>An operator invalid for the property's CLR type is rejected.</summary>
    [Test]
    public void ApplyFilter_RejectsInvalidOperator()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Balance", Operator = "contains", Value = "5" }]
        };

        // Act
        FilterValidationException ex = Assert.Throws<FilterValidationException>(
            () => Seed().ApplyFilter(request).ToList())!;

        // Assert
        Assert.That(ex.ErrorCode, Is.EqualTo(FilterErrorCodes.INVALID_OPERATOR));
    }

    /// <summary>A comparison operator on a non-comparable type (bool) is rejected.</summary>
    [Test]
    public void ApplyFilter_RejectsComparisonOperatorOnBool()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "IsActive", Operator = "gt", Value = "true" }]
        };

        // Act
        FilterValidationException ex = Assert.Throws<FilterValidationException>(
            () => Seed().ApplyFilter(request).ToList())!;

        // Assert
        Assert.That(ex.ErrorCode, Is.EqualTo(FilterErrorCodes.INVALID_OPERATOR));
    }

    /// <summary>An unrecognized operator token is rejected.</summary>
    [Test]
    public void ApplyFilter_RejectsUnknownOperatorToken()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Code", Operator = "matches", Value = "1" }]
        };

        // Act
        FilterValidationException ex = Assert.Throws<FilterValidationException>(
            () => Seed().ApplyFilter(request).ToList())!;

        // Assert
        Assert.That(ex.ErrorCode, Is.EqualTo(FilterErrorCodes.INVALID_OPERATOR));
    }

    /// <summary>A page size above the cap of 200 is rejected.</summary>
    [Test]
    public void ApplyFilter_RejectsPageSizeOver200()
    {
        // Arrange
        FilterRequest request = new() { PageSize = 201 };

        // Act
        FilterValidationException ex = Assert.Throws<FilterValidationException>(
            () => Seed().ApplyFilter(request).ToList())!;

        // Assert
        Assert.That(ex.ErrorCode, Is.EqualTo(FilterErrorCodes.PAGE_SIZE_TOO_LARGE));
    }

    /// <summary>A page size exactly at the cap of 200 is accepted.</summary>
    [Test]
    public void ApplyFilter_AllowsPageSizeAtCap()
    {
        // Arrange
        FilterRequest request = new() { PageSize = 200 };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result, Has.Count.EqualTo(4));
    }

    /// <summary>A <c>between</c> value that is not a 2-element array is rejected.</summary>
    [Test]
    public void ApplyFilter_RejectsBetweenWithWrongElementCount()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Balance", Operator = "between", Value = new[] { "1" } }]
        };

        // Act
        FilterValidationException ex = Assert.Throws<FilterValidationException>(
            () => Seed().ApplyFilter(request).ToList())!;

        // Assert
        Assert.That(ex.ErrorCode, Is.EqualTo(FilterErrorCodes.INVALID_FILTER_VALUE));
    }

    /// <summary>A <c>between</c> value supplied as a scalar (not an array) is rejected.</summary>
    [Test]
    public void ApplyFilter_RejectsBetweenWithNonArrayValue()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Balance", Operator = "between", Value = "100" }]
        };

        // Act
        FilterValidationException ex = Assert.Throws<FilterValidationException>(
            () => Seed().ApplyFilter(request).ToList())!;

        // Assert
        Assert.That(ex.ErrorCode, Is.EqualTo(FilterErrorCodes.INVALID_FILTER_VALUE));
    }

    /// <summary>An <c>in</c> value supplied as a scalar (not an array) is rejected.</summary>
    [Test]
    public void ApplyFilter_RejectsInWithNonArrayValue()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Kind", Operator = "in", Value = "Asset" }]
        };

        // Act
        FilterValidationException ex = Assert.Throws<FilterValidationException>(
            () => Seed().ApplyFilter(request).ToList())!;

        // Assert
        Assert.That(ex.ErrorCode, Is.EqualTo(FilterErrorCodes.INVALID_FILTER_VALUE));
    }

    /// <summary>A value that cannot be parsed into the property type is rejected.</summary>
    [Test]
    public void ApplyFilter_RejectsUnparseableValue()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Balance", Operator = "eq", Value = "not-a-number" }]
        };

        // Act
        FilterValidationException ex = Assert.Throws<FilterValidationException>(
            () => Seed().ApplyFilter(request).ToList())!;

        // Assert
        Assert.That(ex.ErrorCode, Is.EqualTo(FilterErrorCodes.INVALID_FILTER_VALUE));
    }

    /// <summary>An enum value that is not a defined member is rejected.</summary>
    [Test]
    public void ApplyFilter_RejectsUndefinedEnumValue()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Kind", Operator = "eq", Value = "NotAKind" }]
        };

        // Act
        FilterValidationException ex = Assert.Throws<FilterValidationException>(
            () => Seed().ApplyFilter(request).ToList())!;

        // Assert
        Assert.That(ex.ErrorCode, Is.EqualTo(FilterErrorCodes.INVALID_FILTER_VALUE));
    }

    /// <summary>Search ORs a LIKE match across every <c>[Searchable]</c> string property.</summary>
    [Test]
    public void ApplyFilter_SearchOrsAcrossAllSearchableProperties()
    {
        // Arrange
        FilterRequest byName = new() { Search = "ДДС" };
        FilterRequest byCode = new() { Search = "100" };

        // Act
        List<AccountRow> nameResult = Seed().ApplyFilter(byName).ToList();
        List<AccountRow> codeResult = Seed().ApplyFilter(byCode).ToList();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(nameResult.Select(r => r.Id), Is.EqualTo(new[] { 4 }));
            Assert.That(codeResult.Select(r => r.Id), Is.EqualTo(new[] { 1 }));
        });
    }

    /// <summary>A search term matching no searchable property returns no rows.</summary>
    [Test]
    public void ApplyFilter_SearchReturnsEmpty_WhenNoMatch()
    {
        // Arrange
        FilterRequest request = new() { Search = "zzz-no-match" };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result, Is.Empty);
    }

    /// <summary>A raw <see cref="JsonElement"/> filter value is coerced to the property type.</summary>
    [Test]
    public void ApplyFilter_SupportsJsonElementValues()
    {
        // Arrange
        JsonElement json = JsonSerializer.Deserialize<JsonElement>("\"Liability\"");
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Kind", Operator = "eq", Value = json }]
        };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 2 }));
    }

    /// <summary>A raw JSON array <c>in</c> value is expanded and coerced per element.</summary>
    [Test]
    public void ApplyFilter_SupportsJsonElementArrayForIn()
    {
        // Arrange
        JsonElement json = JsonSerializer.Deserialize<JsonElement>("[\"Asset\",\"Liability\"]");
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Kind", Operator = "in", Value = json }]
        };

        // Act
        List<AccountRow> result = Seed().ApplyFilter(request).ToList();

        // Assert
        Assert.That(result.Select(r => r.Id), Is.EqualTo(new[] { 1, 2, 4 }));
    }

    /// <summary><c>ToPagedResult</c> returns the envelope with total count, page, and page size.</summary>
    [Test]
    public void ToPagedResult_ReturnsEnvelopeWithTotalCount()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "IsActive", Operator = "eq", Value = "true" }],
            Page = 1,
            PageSize = 2
        };

        // Act
        PagedResult<AccountRow> result = Seed().ToPagedResult(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.TotalCount, Is.EqualTo(3));
            Assert.That(result.Items, Has.Count.EqualTo(2));
            Assert.That(result.Page, Is.EqualTo(1));
            Assert.That(result.PageSize, Is.EqualTo(2));
        });
    }

    /// <summary>A null source throws <see cref="ArgumentNullException"/>.</summary>
    [Test]
    public void ApplyFilter_ThrowsOnNullSource()
    {
        // Arrange
        IQueryable<AccountRow> source = null!;
        FilterRequest request = new();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => source.ApplyFilter(request));
    }

    /// <summary>A null request throws <see cref="ArgumentNullException"/>.</summary>
    [Test]
    public void ApplyFilter_ThrowsOnNullRequest()
    {
        // Arrange
        IQueryable<AccountRow> source = Seed();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => source.ApplyFilter(null!));
    }
}
