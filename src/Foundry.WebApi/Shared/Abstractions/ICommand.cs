namespace Foundry.WebApi.Shared.Abstractions;

// Marker interface — commands carry data via their concrete record types
#pragma warning disable CA1040
public interface ICommand<TResult>;
#pragma warning restore CA1040
