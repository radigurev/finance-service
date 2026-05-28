namespace Finance.ServiceModel.Accounts;

/// <summary>
/// Request body for updating mutable fields on an existing account.
/// Account code, type, and country code are immutable after creation.
/// </summary>
public sealed record UpdateAccountRequest
{
    /// <summary>Human-readable account name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the account is active and available for posting.</summary>
    public required bool IsActive { get; init; }
}
