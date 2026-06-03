using Finance.Common.ErrorCodes;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit;

/// <summary>
/// Sentinel unit tests confirming the Journal error-code surface required by SDD-FIN-001 / SDD-FIN-002
/// exists. The Phase-3 tester expands the suite to cover the validators, the lifecycle service, and the
/// workflow guards.
/// </summary>
[TestFixture]
[Category("SDD-FIN-002")]
public sealed class JournalErrorCodesTests
{
    /// <summary>The deferred-FIN-004 period-lock seam code MUST exist now (SDD-FIN-002 §2.7).</summary>
    [Test]
    public void JournalErrorCodes_DefinesPostingPeriodClosed_ForDeferredFin004Seam()
    {
        Assert.That(JournalErrorCodes.POSTING_PERIOD_CLOSED, Is.EqualTo("POSTING_PERIOD_CLOSED"));
    }

    /// <summary>The double-entry balance code MUST exist (SDD-FIN-001 §2.3).</summary>
    [Test]
    public void JournalErrorCodes_DefinesUnbalancedEntry()
    {
        Assert.That(JournalErrorCodes.UNBALANCED_ENTRY, Is.EqualTo("UNBALANCED_ENTRY"));
    }
}
