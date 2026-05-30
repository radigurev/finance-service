using Finance.GenericFiltering.Attributes;

namespace Finance.GenericFiltering.Tests.TestEntities;

/// <summary>
/// Test entity exercising every attribute and CLR type the filtering library supports.
/// </summary>
public sealed class AccountRow
{
    /// <summary>Identity key used as the deterministic final sort term.</summary>
    [Sortable]
    public int Id { get; set; }

    /// <summary>Account code.</summary>
    [Filterable]
    [Sortable]
    [Searchable]
    public string Code { get; set; } = string.Empty;

    /// <summary>Account display name.</summary>
    [Filterable]
    [Searchable]
    public string Name { get; set; } = string.Empty;

    /// <summary>Account type as an enum (matched by name).</summary>
    [Filterable]
    [Sortable]
    public AccountKind Kind { get; set; }

    /// <summary>Whether the account is active.</summary>
    [Filterable]
    public bool IsActive { get; set; }

    /// <summary>Opening balance as a monetary amount.</summary>
    [Filterable]
    [Sortable]
    public decimal Balance { get; set; }

    /// <summary>Creation timestamp.</summary>
    [Filterable]
    [Sortable]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Optional parent account id — filterable nullable value type for isNull/isNotNull tests.</summary>
    [Filterable]
    [Sortable]
    public int? ParentId { get; set; }

    /// <summary>Optional notes — not filterable, exercises non-opted-in rejection.</summary>
    public string? Notes { get; set; }
}
