namespace Finance.Journal.API.Interfaces;

/// <summary>
/// A narrow read-only projection of an account's display fields used to enrich GL / trial-balance rows
/// (SDD-FIN-003 §2.5). Carries only the <c>Code</c> / <c>Name</c> resolved through the existing
/// <see cref="IReferenceDataReader"/> seam; it never crosses into the <c>finance_accounts</c> database.
/// </summary>
/// <param name="Code">The country-specific account code.</param>
/// <param name="Name">The human-readable account name.</param>
public sealed record AccountReference(string Code, string Name);
