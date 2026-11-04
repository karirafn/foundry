namespace Foundry.Shared.Infrastructure.DataProtection;

/// <summary>An EF value converter that encrypts its column with Data Protection under a fixed purpose.</summary>
public interface IDataProtectedConverter
{
    string ProtectorPurpose { get; }
}
