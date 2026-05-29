using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Shared.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCommandHandler<TCommand, TResult, THandler, TValidator>(
        this IServiceCollection services)
        where TCommand : ICommand<TResult>
        where TResult : notnull
        where THandler : class, ICommandHandler<TCommand, TResult>
        where TValidator : class, ICommandValidator<TCommand>
    {
        services.AddScoped<THandler>();
        services.AddScoped<ICommandValidator<TCommand>, TValidator>();
        services.AddScoped<ICommandHandler<TCommand, TResult>>(sp =>
            new ValidatingCommandHandler<TCommand, TResult>(
                sp.GetRequiredService<THandler>(),
                sp.GetRequiredService<ICommandValidator<TCommand>>()));
        return services;
    }

    public static IServiceCollection AddCommandHandler<TCommand, TResult, THandler>(
        this IServiceCollection services)
        where TCommand : ICommand<TResult>
        where TResult : notnull
        where THandler : class, ICommandHandler<TCommand, TResult>
    {
        services.AddScoped<ICommandHandler<TCommand, TResult>, THandler>();
        return services;
    }

    public static IServiceCollection AddQueryHandler<TQuery, TResult, THandler>(
        this IServiceCollection services)
        where TQuery : IQuery<TResult>
        where TResult : notnull
        where THandler : class, IQueryHandler<TQuery, TResult>
    {
        services.AddScoped<IQueryHandler<TQuery, TResult>, THandler>();
        return services;
    }

    public static IServiceCollection AddDomainEventHandler<TEvent, THandler>(
        this IServiceCollection services)
        where TEvent : IDomainEvent
        where THandler : class, IDomainEventHandler<TEvent>
    {
        services.AddScoped<IDomainEventHandler<TEvent>, THandler>();
        return services;
    }

    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(
        this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        return services;
    }
}
