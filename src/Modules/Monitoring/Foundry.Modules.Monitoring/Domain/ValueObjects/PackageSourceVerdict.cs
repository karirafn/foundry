namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// Whether Foundry can read a Package Source with one credential, as its last probe found.
/// </summary>
internal abstract record PackageSourceVerdict
{
    private PackageSourceVerdict() { }

    /// <summary>The source has not been probed with this credential yet. Counts as not readable.</summary>
    internal sealed record Unchecked : PackageSourceVerdict;

    /// <summary>The outcome of a probe: <see cref="Readable"/> or <see cref="Unreadable"/>.</summary>
    internal abstract record Probed : PackageSourceVerdict
    {
        private protected Probed() { }
    }

    /// <summary>The service index and its search resource both answered 2xx.</summary>
    internal sealed record Readable : Probed;

    /// <summary>
    /// The probe failed. <see cref="HttpStatus"/> is the status that failed it, or null for a transport failure;
    /// <see cref="Reason"/> is one line, for example "service index returned 401".
    /// </summary>
    internal sealed record Unreadable : Probed
    {
        internal Unreadable(int? httpStatus, string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            HttpStatus = httpStatus;
            Reason = reason;
        }

        public int? HttpStatus { get; }

        public string Reason { get; }
    }
}
