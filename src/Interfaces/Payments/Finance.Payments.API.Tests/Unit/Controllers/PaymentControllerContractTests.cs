using System.Reflection;
using Finance.Payments.API.Controllers;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Warehouse.Auth.AspNetCore.Authorization;

namespace Finance.Payments.API.Tests.Unit.Controllers;

/// <summary>
/// Unit tests over the controller CONTRACT attributes (SDD-PAY-001 §2.17, SDD-PAY-002 §2.13, SDD-PAY-003 §2.9). The
/// 403 behaviour itself belongs to the integration suite, but the DECLARED permission of every action is assertable
/// offline — and it matters because all eight permissions must be seeded MANUALLY in the auth service, with
/// <c>finance.aging:read</c> deliberately distinct so a reporting role can read the roll-ups without the individual
/// payment records.
/// </summary>
[TestFixture]
public sealed class PaymentControllerContractTests
{
    [Test]
    [Category("SDD-PAY-001")]
    public void PaymentsController_EveryAction_DeclaresItsRequiredPermission()
    {
        // Arrange
        IReadOnlyDictionary<string, string> expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["List"] = "finance.payment:read",
            ["Get"] = "finance.payment:read",
            ["Create"] = "finance.payment:create",
            ["Update"] = "finance.payment:create",
            ["Delete"] = "finance.payment:create",
            ["Confirm"] = "finance.payment:confirm",
            ["Post"] = "finance.payment:post",
            ["Cancel"] = "finance.payment:cancel",
            ["Reverse"] = "finance.payment:reverse"
        };

        // Act
        IReadOnlyDictionary<string, string?> declared = DeclaredPermissions(typeof(PaymentsController));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(declared, Has.Count.EqualTo(expected.Count), "nine endpoints, nine permissions");
            foreach (KeyValuePair<string, string> action in expected)
            {
                Assert.That(declared.ContainsKey(action.Key), Is.True, action.Key);
                Assert.That(declared[action.Key], Is.EqualTo(action.Value), action.Key);
            }
        });
    }

    [Test]
    [Category("SDD-PAY-002")]
    public void PaymentAllocationsController_AllocateAndDeallocate_RequireTheAllocatePermission()
    {
        // Arrange
        IReadOnlyDictionary<string, string?> declared =
            DeclaredPermissions(typeof(PaymentAllocationsController));

        // Act
        IReadOnlyList<string?> allocateActions =
            [.. declared.Where(entry => entry.Key != "List").Select(entry => entry.Value)];

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(declared["List"], Is.EqualTo("finance.payment:read"));
            Assert.That(allocateActions, Has.Count.EqualTo(2));
            Assert.That(allocateActions, Is.All.EqualTo("finance.payment:allocate"));
        });
    }

    [Test]
    [Category("SDD-PAY-003")]
    public void AgingControllers_DeclareTheReportLevelAgingPermission_ButOpenItemsReadsAsAPayment()
    {
        // Arrange
        IReadOnlyDictionary<string, string?> openItems = DeclaredPermissions(typeof(OpenItemsController));
        IReadOnlyDictionary<string, string?> aging = DeclaredPermissions(typeof(AgingController));

        // Act
        IReadOnlyDictionary<string, string?> balances =
            DeclaredPermissions(typeof(CounterpartyBalancesController));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(openItems.Values, Is.All.EqualTo("finance.payment:read"));
            Assert.That(aging.Values, Is.All.EqualTo("finance.aging:read"));
            Assert.That(balances.Values, Is.All.EqualTo("finance.aging:read"));
        });
    }

    [Test]
    [Category("SDD-PAY-001")]
    public void EveryAction_DeclaresProducesResponseType_AndTakesCancellationTokenLast()
    {
        // Arrange
        Type[] controllers =
        [
            typeof(PaymentsController),
            typeof(PaymentAllocationsController),
            typeof(OpenItemsController),
            typeof(AgingController),
            typeof(CounterpartyBalancesController)
        ];

        // Act
        IReadOnlyList<MethodInfo> actions = [.. controllers.SelectMany(ActionsOf)];

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(actions, Is.Not.Empty);
            foreach (MethodInfo action in actions)
            {
                Assert.That(
                    action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
                    Is.Not.Empty,
                    action.Name);
                Assert.That(
                    action.GetParameters()[^1].ParameterType,
                    Is.EqualTo(typeof(CancellationToken)),
                    action.Name);
            }
        });
    }

    /// <summary>Reads the declared permission of every action on a controller.</summary>
    /// <param name="controllerType">The controller type.</param>
    /// <returns>The action names mapped to their declared permission.</returns>
    private static IReadOnlyDictionary<string, string?> DeclaredPermissions(Type controllerType)
    {
        return ActionsOf(controllerType).ToDictionary(
            action => action.Name,
            action => action.GetCustomAttribute<RequirePermissionAttribute>()?.Permission,
            StringComparer.Ordinal);
    }

    /// <summary>Reflects the public action methods declared directly on a controller.</summary>
    /// <param name="controllerType">The controller type.</param>
    /// <returns>The action methods.</returns>
    private static IEnumerable<MethodInfo> ActionsOf(Type controllerType) => controllerType
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(method => !method.IsSpecialName);
}
