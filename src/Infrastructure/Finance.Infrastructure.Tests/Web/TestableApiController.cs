using Finance.Common.Results;
using Finance.Infrastructure.Web.Controllers;
using Finance.Infrastructure.Web.ErrorMapping;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Infrastructure.Tests.Web;

/// <summary>
/// Test-only concrete subclass of <see cref="BaseApiController"/> exposing the protected
/// <c>ToActionResult</c> overloads for unit testing.
/// </summary>
public sealed class TestableApiController : BaseApiController
{
    /// <summary>Initializes the controller with the supplied status map.</summary>
    /// <param name="statusMap">The error-code → status map under test.</param>
    public TestableApiController(IErrorCodeToStatusMap statusMap)
        : base(statusMap)
    {
    }

    /// <summary>Exposes the value-bearing translation for testing.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The service outcome.</param>
    /// <returns>The translated action result.</returns>
    public ActionResult<T> Translate<T>(Result<T> result)
    {
        return ToActionResult(result);
    }

    /// <summary>Exposes the void translation for testing.</summary>
    /// <param name="result">The service outcome.</param>
    /// <returns>The translated action result.</returns>
    public ActionResult Translate(Result result)
    {
        return ToActionResult(result);
    }
}
