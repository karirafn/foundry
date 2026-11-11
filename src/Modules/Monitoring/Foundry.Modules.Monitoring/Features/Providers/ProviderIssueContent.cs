using Foundry.Modules.Monitoring.Features.Providers.Feedback;

namespace Foundry.Modules.Monitoring.Features.Providers;

/// <summary>An issue's title and body as its author wrote them, without any Foundry interpretation.</summary>
internal sealed record ProviderIssueText(string Title, string Body);

/// <summary>
/// An issue's own content: its title, body and every comment a person wrote on its thread, oldest first.
/// Provider system notes (label and state changes) are left out.
/// </summary>
internal sealed record ProviderIssueContent(string Title, string Body, IReadOnlyList<ProviderComment> Comments);
