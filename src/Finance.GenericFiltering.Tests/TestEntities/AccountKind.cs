namespace Finance.GenericFiltering.Tests.TestEntities;

/// <summary>Classification used by <see cref="AccountRow"/> in filtering tests.</summary>
public enum AccountKind
{
    /// <summary>Asset account.</summary>
    Asset = 1,

    /// <summary>Liability account.</summary>
    Liability = 2,

    /// <summary>Equity account.</summary>
    Equity = 3
}
