using Kanoonyar.Contracts.Programs;

namespace Kanoonyar.Contracts.Programs;

/// <summary>Deterministic, in-memory catalog for immutable national program versions.
/// No persistence, publication authority or permission checks are implied.</summary>
public sealed class NationalProgramCatalog
{
    private readonly Dictionary<(string ProgramId, int Version), NationalProgramVersion> _versions = new();

    public void Add(NationalProgramVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (!_versions.TryAdd((version.ProgramId, version.Version), version))
            throw new InvalidOperationException("National program version already exists.");
    }

    public NationalProgramVersion? Find(string programId, int version)
    {
        if (string.IsNullOrWhiteSpace(programId) || version <= 0) return null;
        return _versions.GetValueOrDefault((programId, version));
    }

    public IReadOnlyList<NationalProgramVersion> ListVersions(string programId)
    {
        if (string.IsNullOrWhiteSpace(programId)) return Array.Empty<NationalProgramVersion>();
        return _versions.Values.Where(item => StringComparer.Ordinal.Equals(item.ProgramId, programId))
            .OrderBy(item => item.Version).ToArray();
    }
}
