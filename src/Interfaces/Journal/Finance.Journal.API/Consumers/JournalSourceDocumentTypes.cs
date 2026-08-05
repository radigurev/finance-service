namespace Finance.Journal.API.Consumers;

/// <summary>
/// The <c>SourceDocumentType</c> tag values stamped on a journal entry posted for a financial document
/// (SDD-PAY-001 §2.5). Together with the source-document id they form the duplicate-post backstop enforced by
/// the UNIQUE FILTERED index <c>IX_JournalEntries_SourceDocument</c>: at most one <c>Posted</c> entry may ever
/// exist per source document.
/// </summary>
public static class JournalSourceDocumentTypes
{
    /// <summary>A payment source document (customer receipt / supplier payment).</summary>
    public const string Payment = "Payment";

    /// <summary>An invoice source document (invoice / credit note / debit note).</summary>
    public const string Invoice = "Invoice";
}
