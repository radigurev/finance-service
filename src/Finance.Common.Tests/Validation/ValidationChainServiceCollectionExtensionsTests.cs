using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Finance.Common.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="ValidationChainServiceCollectionExtensions.AddValidationChain{TRequest}"/>.
/// Verifies scoped registration of the composer and its validators in order (SDD-INFRA-007).
/// </summary>
[TestFixture]
[Category("SDD-INFRA-007")]
public sealed class ValidationChainServiceCollectionExtensionsTests
{
    /// <summary>The composer resolves with its registered validators in registration order.</summary>
    [Test]
    public async Task AddValidationChain_RegistersComposerWithValidatorsInOrder()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddValidationChain<SampleRequest>(typeof(PassValidator), typeof(FailValidator));

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        ValidationChain<SampleRequest> chain =
            scope.ServiceProvider.GetRequiredService<ValidationChain<SampleRequest>>();

        // Act
        ChainValidationResult result = await chain.ValidateAsync(new SampleRequest(), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(CommonErrorCodes.VALIDATION_FAILED));
        });
    }

    /// <summary>The composer is registered with a scoped lifetime.</summary>
    [Test]
    public void AddValidationChain_RegistersComposerAsScoped()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddValidationChain<SampleRequest>(typeof(PassValidator));

        // Act
        ServiceDescriptor descriptor = services.Single(d => d.ServiceType == typeof(ValidationChain<SampleRequest>));

        // Assert
        Assert.That(descriptor.Lifetime, Is.EqualTo(ServiceLifetime.Scoped));
    }

    /// <summary>Each registered validator is added with a scoped lifetime.</summary>
    [Test]
    public void AddValidationChain_RegistersValidatorsAsScoped()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddValidationChain<SampleRequest>(typeof(PassValidator), typeof(FailValidator));

        // Act
        IReadOnlyList<ServiceDescriptor> validatorDescriptors = services
            .Where(d => d.ServiceType == typeof(IChainValidator<SampleRequest>))
            .ToList();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(validatorDescriptors, Has.Count.EqualTo(2));
            Assert.That(validatorDescriptors.All(d => d.Lifetime == ServiceLifetime.Scoped), Is.True);
        });
    }

    /// <summary>Registering a type that does not implement the validator interface throws.</summary>
    [Test]
    public void AddValidationChain_Throws_WhenTypeDoesNotImplementValidator()
    {
        // Arrange
        ServiceCollection services = new();

        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => services.AddValidationChain<SampleRequest>(typeof(NotAValidator)));
    }

    /// <summary>A sample request used only by these tests.</summary>
    private sealed record SampleRequest;

    /// <summary>A validator that always passes.</summary>
    private sealed class PassValidator : IChainValidator<SampleRequest>
    {
        /// <summary>Returns success.</summary>
        /// <param name="request">The request (ignored).</param>
        /// <param name="ct">A token to observe for cancellation (ignored).</param>
        /// <returns>A successful <see cref="ChainValidationResult"/>.</returns>
        public Task<ChainValidationResult> ValidateAsync(SampleRequest request, CancellationToken ct)
            => Task.FromResult(ChainValidationResult.Success());
    }

    /// <summary>A validator that always fails.</summary>
    private sealed class FailValidator : IChainValidator<SampleRequest>
    {
        /// <summary>Returns failure with the generic validation code.</summary>
        /// <param name="request">The request (ignored).</param>
        /// <param name="ct">A token to observe for cancellation (ignored).</param>
        /// <returns>A failing <see cref="ChainValidationResult"/>.</returns>
        public Task<ChainValidationResult> ValidateAsync(SampleRequest request, CancellationToken ct)
            => Task.FromResult(ChainValidationResult.Failure(CommonErrorCodes.VALIDATION_FAILED));
    }

    /// <summary>A type that does not implement the validator interface.</summary>
    private sealed class NotAValidator;
}
