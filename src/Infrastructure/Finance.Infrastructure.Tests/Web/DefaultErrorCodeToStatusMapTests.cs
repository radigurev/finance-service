using Finance.Infrastructure.Web.ErrorMapping;
using NUnit.Framework;

namespace Finance.Infrastructure.Tests.Web;

/// <summary>
/// Unit tests for <see cref="DefaultErrorCodeToStatusMap"/> covering the SDD-INFRA-009 §2.4 mapping rules.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-009")]
public sealed class DefaultErrorCodeToStatusMapTests
{
    private DefaultErrorCodeToStatusMap _map = null!;

    /// <summary>Creates a fresh map before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _map = new DefaultErrorCodeToStatusMap();
    }

    /// <summary>A <c>*_NOT_FOUND</c> code maps to 404.</summary>
    [Test]
    public void DefaultErrorCodeToStatusMap_MapsNotFoundTo404()
    {
        // Arrange
        const string errorCode = "ACCOUNT_NOT_FOUND";

        // Act
        int status = _map.MapToStatus(errorCode);

        // Assert
        Assert.That(status, Is.EqualTo(404));
    }

    /// <summary>The conflict family (<c>*_INACTIVE</c>, <c>*_DUPLICATE*</c>, <c>*_CONFLICT</c>, <c>CONCURRENT_*</c>) maps to 409.</summary>
    [TestCase("ACCOUNT_INACTIVE")]
    [TestCase("DUPLICATE_CURRENCY_CODE")]
    [TestCase("PERIOD_CONFLICT")]
    [TestCase("CONCURRENT_MODIFICATION")]
    public void DefaultErrorCodeToStatusMap_MapsConflictFamilyTo409(string errorCode)
    {
        // Arrange & Act
        int status = _map.MapToStatus(errorCode);

        // Assert
        Assert.That(status, Is.EqualTo(409));
    }

    /// <summary>The forbidden family (<c>*_FORBIDDEN</c>, <c>INSUFFICIENT_*</c>) maps to 403.</summary>
    [TestCase("OPERATION_FORBIDDEN")]
    [TestCase("INSUFFICIENT_PERMISSIONS")]
    public void DefaultErrorCodeToStatusMap_MapsForbiddenFamilyTo403(string errorCode)
    {
        // Arrange & Act
        int status = _map.MapToStatus(errorCode);

        // Assert
        Assert.That(status, Is.EqualTo(403));
    }

    /// <summary>A <c>*_UNREACHABLE</c> code maps to 503.</summary>
    [Test]
    public void DefaultErrorCodeToStatusMap_MapsUnreachableTo503()
    {
        // Arrange
        const string errorCode = "WAREHOUSE_NOMENCLATURE_UNREACHABLE";

        // Act
        int status = _map.MapToStatus(errorCode);

        // Assert
        Assert.That(status, Is.EqualTo(503));
    }

    /// <summary>Any unmatched code maps to 400.</summary>
    [TestCase("VALIDATION_FAILED")]
    [TestCase("INVALID_FILTER_FIELD")]
    [TestCase("")]
    public void DefaultErrorCodeToStatusMap_MapsUnknownCodeTo400(string errorCode)
    {
        // Arrange & Act
        int status = _map.MapToStatus(errorCode);

        // Assert
        Assert.That(status, Is.EqualTo(400));
    }
}
