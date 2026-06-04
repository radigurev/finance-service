using Finance.Common.ErrorCodes;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit;

/// <summary>
/// Unit tests confirming the eight posting error codes required by SDD-FIN-006 §4 exist and carry their
/// SCREAMING_SNAKE_CASE machine values. These codes back the ProblemDetails <c>title</c> and the
/// <c>.WithErrorCode(...)</c> calls; no raw string literals are used in the implementation.
/// </summary>
[TestFixture]
[Category("SDD-FIN-006")]
public sealed class PostingErrorCodesTests
{
    [Test]
    public void PostingErrorCodes_DefinesAllEightCodes()
    {
        // Arrange
        string[] expected =
        [
            "POSTING_RULE_NOT_FOUND",
            "DUPLICATE_POSTING_RULE_KEY",
            "POSTING_RULE_HAS_NO_LINES",
            "POSTING_RULE_UNBALANCED",
            "MISSING_POSTING_AMOUNT",
            "INVALID_POSTING_RULE_KEY",
            "INVALID_POSTING_RULE_LINE",
            "POSTING_RULE_ACCOUNT_NOT_FOUND"
        ];

        // Act
        string[] actual =
        [
            PostingErrorCodes.POSTING_RULE_NOT_FOUND,
            PostingErrorCodes.DUPLICATE_POSTING_RULE_KEY,
            PostingErrorCodes.POSTING_RULE_HAS_NO_LINES,
            PostingErrorCodes.POSTING_RULE_UNBALANCED,
            PostingErrorCodes.MISSING_POSTING_AMOUNT,
            PostingErrorCodes.INVALID_POSTING_RULE_KEY,
            PostingErrorCodes.INVALID_POSTING_RULE_LINE,
            PostingErrorCodes.POSTING_RULE_ACCOUNT_NOT_FOUND
        ];

        // Assert
        Assert.That(actual, Is.EqualTo(expected));
    }
}
