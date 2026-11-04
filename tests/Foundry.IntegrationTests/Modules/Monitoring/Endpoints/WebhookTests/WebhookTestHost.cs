using System.Text;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Modules.Settings.Domain.Entities;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.WebhookTests;

/// <summary>
/// Shared arrange/act helpers for webhook ingress tests. Requests are sent through
/// <see cref="TestServer.SendAsync(Action{HttpContext}, CancellationToken)"/> so each test controls the
/// connection's local port — the signal the two-way port guard uses (ADR 0085).
/// </summary>
internal static class WebhookTestHost
{
    internal const int WebhookPort = 5099;
    internal const int MainPort = 5000;

    internal static FoundryWebAppFactory CreateFactory() =>
        FoundryWebAppFactory.WithOverrides(services =>
            services.Configure<WebhookIngressOptions>(options => options.Port = WebhookPort));

    internal static async Task<HttpContext> SendAsync(
        FoundryWebAppFactory factory,
        string method,
        string path,
        int localPort,
        string? body = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        return await factory.Server.SendAsync(context =>
        {
            context.Request.Method = method;
            context.Request.Path = path;
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("localhost", localPort);
            context.Connection.LocalPort = localPort;

            if (body is not null)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                context.Request.Body = new MemoryStream(bytes);
                context.Request.ContentLength = bytes.Length;
                context.Request.ContentType = "application/json";
            }

            foreach ((string name, string value) in headers ?? new Dictionary<string, string>())
            {
                context.Request.Headers[name] = value;
            }
        });
    }

    internal static Task<HttpContext> DeliverAsync(
        FoundryWebAppFactory factory,
        string hookId,
        string eventName,
        string body,
        string? secret,
        int localPort = WebhookPort,
        string pathPrefix = "/webhooks/github/")
    {
        Dictionary<string, string> headers = new() { [GitHubWebhookEventMapper.EventHeaderName] = eventName };
        if (secret is not null)
        {
            headers[GitHubWebhookSignature.HeaderName] = GitHubWebhookSignature.Compute(Encoding.UTF8.GetBytes(body), secret);
        }

        return SendAsync(factory, HttpMethods.Post, $"{pathPrefix}{hookId}", localPort, body, headers);
    }

    internal static Task<HttpContext> DeliverGitLabAsync(
        FoundryWebAppFactory factory,
        string hookId,
        string eventName,
        string body,
        string? token,
        int localPort = WebhookPort)
    {
        Dictionary<string, string> headers = new() { [GitLabWebhookEventMapper.EventHeaderName] = eventName };
        if (token is not null)
        {
            headers[GitLabWebhookToken.HeaderName] = token;
        }

        return SendAsync(factory, HttpMethods.Post, $"/webhooks/gitlab/{hookId}", localPort, body, headers);
    }

    internal static async Task<MonitoredRepository> SeedRepositoryAsync(
        FoundryWebAppFactory factory,
        string slug = "owner/repo",
        bool registered = false,
        string host = "github.com")
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create(slug).ValueOrThrow(),
            host,
            null).ValueOrThrow();

        if (registered)
        {
            repository.MarkWebhookRegistered();
        }

        dbContext.Set<MonitoredRepository>().Add(repository);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return repository;
    }

    internal static async Task SeedSettingsAsync(FoundryWebAppFactory factory, string? publicBaseUrl)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        settings.UpdateWebhookSettings(publicBaseUrl, 15);
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    internal static async Task<MonitoredRepository> LoadRepositoryAsync(FoundryWebAppFactory factory, MonitoredRepository seeded)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        return await dbContext.Set<MonitoredRepository>().AsNoTracking().SingleAsync(r => r.Id == seeded.Id);
    }

    internal static List<Nudge> DrainNudges(FoundryWebAppFactory factory)
    {
        WebhookNudgeQueue queue = factory.Services.GetRequiredService<WebhookNudgeQueue>();
        List<Nudge> nudges = [];
        while (queue.Reader.TryRead(out Nudge nudge))
        {
            nudges.Add(nudge);
        }

        return nudges;
    }
}
