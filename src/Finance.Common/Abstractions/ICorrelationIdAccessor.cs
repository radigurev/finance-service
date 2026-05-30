namespace Finance.Common.Abstractions;

/// <summary>
/// Provides read access to the ambient correlation identifier for the current logical operation.
/// <para>This abstraction is intentionally free of any ASP.NET / web dependency so that domain and
/// service assemblies (e.g. <c>Finance.Infrastructure.Services</c>) can depend on it. The HTTP-backed
/// implementation (<c>HttpContextCorrelationIdAccessor</c>) lives in <c>Finance.Infrastructure.Web</c>
/// and reads the id stamped by the <c>Warehouse.Correlation</c> middleware (SDD-INFRA-001, SDD-OBS-001).</para>
/// </summary>
public interface ICorrelationIdAccessor
{
    /// <summary>Gets the ambient correlation identifier, generating one when none is present.</summary>
    /// <returns>The RFC 4122 correlation identifier for the current operation.</returns>
    string Get();
}
