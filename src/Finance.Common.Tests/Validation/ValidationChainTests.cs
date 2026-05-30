using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using NUnit.Framework;

namespace Finance.Common.Tests.Validation;

/// <summary>
/// Unit tests for the <see cref="ValidationChain{TRequest}"/> composer.
/// Covers the SDD-INFRA-007 Batch-1 chain-mechanic test plan.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-007")]
public sealed class ValidationChainTests
{
    /// <summary>Validators run in registration order, recorded by each as it executes.</summary>
    [Test]
    public async Task Chain_RunsValidatorsInRegistrationOrder()
    {
        // Arrange
        List<string> log = [];
        ValidationChain<SampleRequest> chain = new(
        [
            new RecordingValidator("first", log, ChainValidationResult.Success()),
            new RecordingValidator("second", log, ChainValidationResult.Success()),
            new RecordingValidator("third", log, ChainValidationResult.Success())
        ]);

        // Act
        await chain.ValidateAsync(new SampleRequest(), CancellationToken.None);

        // Assert
        Assert.That(log, Is.EqualTo(new[] { "first", "second", "third" }));
    }

    /// <summary>The first failure short-circuits the chain; later validators do not run.</summary>
    [Test]
    public async Task Chain_ShortCircuitsOnFirstFailure()
    {
        // Arrange
        List<string> log = [];
        ChainValidationResult failure = ChainValidationResult.Failure(CommonErrorCodes.VALIDATION_FAILED, "stop");
        ValidationChain<SampleRequest> chain = new(
        [
            new RecordingValidator("first", log, ChainValidationResult.Success()),
            new RecordingValidator("second", log, failure),
            new RecordingValidator("third", log, ChainValidationResult.Success())
        ]);

        // Act
        ChainValidationResult result = await chain.ValidateAsync(new SampleRequest(), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(CommonErrorCodes.VALIDATION_FAILED));
            Assert.That(result.Detail, Is.EqualTo("stop"));
            Assert.That(log, Is.EqualTo(new[] { "first", "second" }));
        });
    }

    /// <summary>The chain returns the failing validator's exact error code, not a generic one.</summary>
    [Test]
    public async Task Chain_ReturnsFailingValidatorsCode()
    {
        // Arrange
        List<string> log = [];
        ChainValidationResult failure = ChainValidationResult.Failure(CommonErrorCodes.CONCURRENT_MODIFICATION, "boom");
        ValidationChain<SampleRequest> chain = new(
        [
            new RecordingValidator("only", log, failure)
        ]);

        // Act
        ChainValidationResult result = await chain.ValidateAsync(new SampleRequest(), CancellationToken.None);

        // Assert
        Assert.That(result.ErrorCode, Is.EqualTo(CommonErrorCodes.CONCURRENT_MODIFICATION));
    }

    /// <summary>The supplied cancellation token is forwarded to each validator.</summary>
    [Test]
    public async Task Chain_PassesCancellationTokenToValidators()
    {
        // Arrange
        using CancellationTokenSource cts = new();
        TokenCapturingValidator validator = new();
        ValidationChain<SampleRequest> chain = new([validator]);

        // Act
        await chain.ValidateAsync(new SampleRequest(), cts.Token);

        // Assert
        Assert.That(validator.CapturedToken, Is.EqualTo(cts.Token));
    }

    /// <summary>When all validators pass the chain returns success.</summary>
    [Test]
    public async Task Chain_ReturnsSuccess_WhenAllValidatorsPass()
    {
        // Arrange
        List<string> log = [];
        ValidationChain<SampleRequest> chain = new(
        [
            new RecordingValidator("a", log, ChainValidationResult.Success()),
            new RecordingValidator("b", log, ChainValidationResult.Success())
        ]);

        // Act
        ChainValidationResult result = await chain.ValidateAsync(new SampleRequest(), CancellationToken.None);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    /// <summary>An empty validator set passes (documented Batch-1 deviation: no startup-throw subsystem).</summary>
    [Test]
    public async Task Chain_ReturnsSuccess_WhenNoValidatorsRegistered()
    {
        // Arrange
        ValidationChain<SampleRequest> chain = new([]);

        // Act
        ChainValidationResult result = await chain.ValidateAsync(new SampleRequest(), CancellationToken.None);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    /// <summary>Constructing the chain with a null validator enumerable throws.</summary>
    [Test]
    public void Chain_Constructor_ThrowsOnNullValidators()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => _ = new ValidationChain<SampleRequest>(null!));
    }

    /// <summary>A sample request used only by these tests.</summary>
    private sealed record SampleRequest;

    /// <summary>A validator that records its name on execution and returns a preset result.</summary>
    private sealed class RecordingValidator : IChainValidator<SampleRequest>
    {
        private readonly string _name;
        private readonly List<string> _log;
        private readonly ChainValidationResult _result;

        /// <summary>Initializes the recording validator.</summary>
        /// <param name="name">The marker recorded into the shared log on execution.</param>
        /// <param name="log">The shared execution log.</param>
        /// <param name="result">The preset result this validator returns.</param>
        public RecordingValidator(string name, List<string> log, ChainValidationResult result)
        {
            _name = name;
            _log = log;
            _result = result;
        }

        /// <summary>Records execution and returns the preset result.</summary>
        /// <param name="request">The request (ignored).</param>
        /// <param name="ct">A token to observe for cancellation (ignored).</param>
        /// <returns>The preset <see cref="ChainValidationResult"/>.</returns>
        public Task<ChainValidationResult> ValidateAsync(SampleRequest request, CancellationToken ct)
        {
            _log.Add(_name);
            return Task.FromResult(_result);
        }
    }

    /// <summary>A validator that captures the cancellation token it was invoked with.</summary>
    private sealed class TokenCapturingValidator : IChainValidator<SampleRequest>
    {
        /// <summary>The token captured during the last <see cref="ValidateAsync"/> call.</summary>
        public CancellationToken CapturedToken { get; private set; }

        /// <summary>Captures the supplied token and returns success.</summary>
        /// <param name="request">The request (ignored).</param>
        /// <param name="ct">A token to observe for cancellation; captured for assertion.</param>
        /// <returns>A successful <see cref="ChainValidationResult"/>.</returns>
        public Task<ChainValidationResult> ValidateAsync(SampleRequest request, CancellationToken ct)
        {
            CapturedToken = ct;
            return Task.FromResult(ChainValidationResult.Success());
        }
    }
}
