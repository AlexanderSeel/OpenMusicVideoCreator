using OpenMusicVideoCreator.Application.Providers;

namespace OpenMusicVideoCreator.Infrastructure.Providers;

public sealed class CredentialResolver : ICredentialResolver
{
    public ValueTask<ResolvedCredential?> ResolveAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        return reference.Kind switch
        {
            CredentialReferenceKind.Environment =>
                ValueTask.FromResult(ResolveEnvironment(reference.Identifier)),
            CredentialReferenceKind.OperatingSystem or CredentialReferenceKind.External =>
                ValueTask.FromResult<ResolvedCredential?>(null),
            _ => throw new ArgumentOutOfRangeException(nameof(reference), reference.Kind, "Unknown credential reference kind."),
        };
    }

    private static ResolvedCredential? ResolveEnvironment(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Environment credential identifier is required.", nameof(identifier));
        }

        var value = Environment.GetEnvironmentVariable(identifier);
        return string.IsNullOrWhiteSpace(value) ? null : new ResolvedCredential(value);
    }
}
