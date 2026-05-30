using Finance.Common.Enums;
using Finance.ServiceModel.Events.Accounts;
using Finance.ServiceModel.Events.Nomenclature;

namespace Finance.EventLog.API.Tests.Builders;

/// <summary>
/// Builds deterministic Finance domain-event instances for the EventLog unit tests (SDD-EVTLOG-001 §6).
/// Covers all six consumed events (Account / Currency create, update, deactivate).
/// </summary>
public static class TestEventBuilder
{
    /// <summary>The deterministic correlation id stamped onto built events.</summary>
    public const string CorrelationId = "test-correlation-id";

    /// <summary>Builds an <see cref="AccountCreatedEvent"/> with the supplied identifiers.</summary>
    /// <param name="messageId">The message identifier carried by the event.</param>
    /// <param name="occurredAt">The instant the originating change occurred.</param>
    /// <returns>A populated <see cref="AccountCreatedEvent"/>.</returns>
    public static AccountCreatedEvent AccountCreated(Guid messageId, DateTimeOffset occurredAt)
    {
        return new AccountCreatedEvent
        {
            MessageId = messageId,
            CorrelationId = CorrelationId,
            OccurredAt = occurredAt,
            AccountId = 1042,
            Code = "304",
            Name = "Goods",
            Type = AccountType.Asset,
            CountryCode = "BG",
            IsActive = true
        };
    }

    /// <summary>Builds an <see cref="AccountUpdatedEvent"/> with the supplied identifiers.</summary>
    /// <param name="messageId">The message identifier carried by the event.</param>
    /// <param name="occurredAt">The instant the originating change occurred.</param>
    /// <returns>A populated <see cref="AccountUpdatedEvent"/>.</returns>
    public static AccountUpdatedEvent AccountUpdated(Guid messageId, DateTimeOffset occurredAt)
    {
        return new AccountUpdatedEvent
        {
            MessageId = messageId,
            CorrelationId = CorrelationId,
            OccurredAt = occurredAt,
            AccountId = 1042,
            Code = "304",
            Name = "Goods (renamed)",
            Type = AccountType.Asset,
            CountryCode = "BG",
            IsActive = true
        };
    }

    /// <summary>Builds an <see cref="AccountDeactivatedEvent"/> with the supplied identifiers.</summary>
    /// <param name="messageId">The message identifier carried by the event.</param>
    /// <param name="occurredAt">The instant the originating change occurred.</param>
    /// <returns>A populated <see cref="AccountDeactivatedEvent"/>.</returns>
    public static AccountDeactivatedEvent AccountDeactivated(Guid messageId, DateTimeOffset occurredAt)
    {
        return new AccountDeactivatedEvent
        {
            MessageId = messageId,
            CorrelationId = CorrelationId,
            OccurredAt = occurredAt,
            AccountId = 1042,
            Code = "304",
            Name = "Goods",
            Type = AccountType.Asset,
            CountryCode = "BG",
            IsActive = false
        };
    }

    /// <summary>Builds a <see cref="CurrencyCreatedEvent"/> with the supplied identifiers.</summary>
    /// <param name="messageId">The message identifier carried by the event.</param>
    /// <param name="occurredAt">The instant the originating change occurred.</param>
    /// <returns>A populated <see cref="CurrencyCreatedEvent"/>.</returns>
    public static CurrencyCreatedEvent CurrencyCreated(Guid messageId, DateTimeOffset occurredAt)
    {
        return new CurrencyCreatedEvent
        {
            MessageId = messageId,
            CorrelationId = CorrelationId,
            OccurredAt = occurredAt,
            CurrencyId = 7,
            IsoCode = "BGN",
            Name = "Bulgarian Lev",
            Symbol = "лв",
            IsActive = true
        };
    }

    /// <summary>Builds a <see cref="CurrencyUpdatedEvent"/> with the supplied identifiers.</summary>
    /// <param name="messageId">The message identifier carried by the event.</param>
    /// <param name="occurredAt">The instant the originating change occurred.</param>
    /// <returns>A populated <see cref="CurrencyUpdatedEvent"/>.</returns>
    public static CurrencyUpdatedEvent CurrencyUpdated(Guid messageId, DateTimeOffset occurredAt)
    {
        return new CurrencyUpdatedEvent
        {
            MessageId = messageId,
            CorrelationId = CorrelationId,
            OccurredAt = occurredAt,
            CurrencyId = 7,
            IsoCode = "BGN",
            Name = "Bulgarian Lev (renamed)",
            Symbol = "лв",
            IsActive = true
        };
    }

    /// <summary>Builds a <see cref="CurrencyDeactivatedEvent"/> with the supplied identifiers.</summary>
    /// <param name="messageId">The message identifier carried by the event.</param>
    /// <param name="occurredAt">The instant the originating change occurred.</param>
    /// <returns>A populated <see cref="CurrencyDeactivatedEvent"/>.</returns>
    public static CurrencyDeactivatedEvent CurrencyDeactivated(Guid messageId, DateTimeOffset occurredAt)
    {
        return new CurrencyDeactivatedEvent
        {
            MessageId = messageId,
            CorrelationId = CorrelationId,
            OccurredAt = occurredAt,
            CurrencyId = 7,
            IsoCode = "BGN",
            Name = "Bulgarian Lev",
            Symbol = "лв",
            IsActive = false
        };
    }
}
