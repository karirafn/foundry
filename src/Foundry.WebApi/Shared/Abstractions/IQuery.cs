namespace Foundry.WebApi.Shared.Abstractions;

// Marker interface — queries carry data via their concrete record types
#pragma warning disable CA1040
public interface IQuery<TResult>;
#pragma warning restore CA1040
