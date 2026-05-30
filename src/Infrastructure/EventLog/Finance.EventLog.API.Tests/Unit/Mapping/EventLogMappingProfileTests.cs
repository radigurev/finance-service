using AutoMapper;
using Finance.EventLog.API.Mapping;
using Finance.EventLog.DBModel.Models;
using Finance.ServiceModel.EventLog;
using NUnit.Framework;

namespace Finance.EventLog.API.Tests.Unit.Mapping;

/// <summary>
/// Unit tests for <see cref="EventLogMappingProfile"/> (SDD-EVTLOG-001 §2.4): the profile configuration is
/// valid and projects an <see cref="EventLogEntry"/> archive row onto the read-only
/// <see cref="EventLogEntryDto"/> exposed by the query API. Runs fully offline.
/// </summary>
[TestFixture]
[Category("SDD-EVTLOG-001")]
public sealed class EventLogMappingProfileTests
{
    private IMapper _mapper = null!;

    /// <summary>Builds a mapper from the profile under test before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        MapperConfiguration configuration = new(cfg => cfg.AddProfile<EventLogMappingProfile>());
        _mapper = configuration.CreateMapper();
    }

    /// <summary>The profile is internally consistent (SDD-EVTLOG-001 §2.4).</summary>
    [Test]
    public void Configuration_EventLogMappingProfile_IsValid()
    {
        // Arrange
        MapperConfiguration configuration = new(cfg => cfg.AddProfile<EventLogMappingProfile>());

        // Act + Assert
        Assert.That(() => configuration.AssertConfigurationIsValid(), Throws.Nothing);
    }

    /// <summary>An entry projects every field onto the DTO (SDD-EVTLOG-001 §2.4).</summary>
    [Test]
    public void Map_EventLogEntry_ProjectsAllFieldsToDto()
    {
        // Arrange
        DateTimeOffset occurredAt = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset receivedAt = occurredAt.AddSeconds(2);
        EventLogEntry entry = new()
        {
            Id = 42,
            EventId = Guid.NewGuid(),
            EventType = "AccountCreatedEvent",
            SourceService = EventLogSourceServices.Accounts,
            OccurredAt = occurredAt,
            ReceivedAt = receivedAt,
            CorrelationId = "trace-1",
            PayloadJson = "{\"accountId\":1042}"
        };

        // Act
        EventLogEntryDto dto = _mapper.Map<EventLogEntryDto>(entry);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(dto.Id, Is.EqualTo(entry.Id));
            Assert.That(dto.EventId, Is.EqualTo(entry.EventId));
            Assert.That(dto.EventType, Is.EqualTo(entry.EventType));
            Assert.That(dto.SourceService, Is.EqualTo(entry.SourceService));
            Assert.That(dto.OccurredAt, Is.EqualTo(entry.OccurredAt));
            Assert.That(dto.ReceivedAt, Is.EqualTo(entry.ReceivedAt));
            Assert.That(dto.CorrelationId, Is.EqualTo(entry.CorrelationId));
            Assert.That(dto.PayloadJson, Is.EqualTo(entry.PayloadJson));
        });
    }
}
