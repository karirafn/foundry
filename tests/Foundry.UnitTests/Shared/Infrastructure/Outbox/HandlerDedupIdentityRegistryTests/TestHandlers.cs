using Foundry.Shared;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.HandlerDedupIdentityRegistryTests;

[IntegrationEventHandlerIdentity("handler-alpha")]
internal sealed class AnnotatedHandlerAlpha;

[IntegrationEventHandlerIdentity("handler-beta")]
internal sealed class AnnotatedHandlerBeta;

[IntegrationEventHandlerIdentity("handler-alpha")]
internal sealed class CollidingHandler;

internal sealed class UnannotatedHandler;

[IntegrationEventHandlerIdentity("handler-beta")]
internal sealed class CollidingHandlerBeta;

[IntegrationEventHandlerIdentity("handler-gamma")]
internal sealed class AnnotatedHandlerGamma;

[IntegrationEventHandlerIdentity("handler-gamma")]
internal sealed class CollidingHandlerGamma;
