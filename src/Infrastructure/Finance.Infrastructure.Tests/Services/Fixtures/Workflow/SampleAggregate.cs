namespace Finance.Infrastructure.Tests.Services.Fixtures.Workflow;

/// <summary>A sample aggregate with a string state field used by the workflow engine tests.</summary>
public sealed class SampleAggregate
{
    /// <summary>The current state name of the aggregate.</summary>
    public string State { get; set; } = "Draft";
}
