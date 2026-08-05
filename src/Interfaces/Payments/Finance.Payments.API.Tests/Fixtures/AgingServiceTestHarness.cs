using AutoMapper;
using Finance.Payments.API.Services;
using Finance.Payments.DBModel;
using Microsoft.Extensions.Logging.Abstractions;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// Assembles an <see cref="AgingService"/> over a SQLite in-memory <see cref="PaymentsDbContext"/> with the REAL
/// pure <see cref="AgingBucketCalculator"/> and the REAL <see cref="SettlementStatusCalculator"/>, plus a fake
/// country strategy owning base-currency rounding and a settable clock (SDD-PAY-003 §6.1-§6.5).
/// <para>No workflow engine, audit service, publish endpoint, sequence generator, or cache service is wired —
/// because <see cref="AgingService"/> takes none: it is a read-only aggregation (SDD-PAY-003 §2.9).</para>
/// </summary>
public sealed class AgingServiceTestHarness
{
    private AgingServiceTestHarness(
        PaymentsDbContext db,
        AgingService service,
        FakePaymentCountryStrategy country,
        FixedTimeProvider clock)
    {
        Db = db;
        Service = service;
        Country = country;
        Clock = clock;
    }

    /// <summary>The SQLite-backed payments context under test.</summary>
    public PaymentsDbContext Db { get; }

    /// <summary>The system under test.</summary>
    public AgingService Service { get; }

    /// <summary>The fake country strategy owning base-currency rounding and the reporting base currency.</summary>
    public FakePaymentCountryStrategy Country { get; }

    /// <summary>The settable clock supplying "today" for the as-of path choice and the future-date guard.</summary>
    public FixedTimeProvider Clock { get; }

    /// <summary>Builds a harness over the supplied context.</summary>
    /// <param name="db">The SQLite-backed payments context.</param>
    /// <returns>A wired harness.</returns>
    public static AgingServiceTestHarness Build(PaymentsDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        IMapper mapper = PaymentTestMapper.Create();
        FakePaymentCountryStrategy country = new();
        FixedTimeProvider clock = new();

        AgingService service = new(
            db,
            mapper,
            new AgingBucketCalculator(),
            new SettlementStatusCalculator(),
            country,
            clock,
            NullLogger<AgingService>.Instance);

        return new AgingServiceTestHarness(db, service, country, clock);
    }
}
