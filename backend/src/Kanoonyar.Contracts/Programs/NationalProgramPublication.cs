namespace Kanoonyar.Contracts.Programs;

/// <summary>Immutable publication metadata. It is not proof that a human is authorized.</summary>
public sealed record NationalProgramPublication
{
    public string ProgramId { get; }
    public int Version { get; }
    public string PublisherSubjectId { get; }
    public DateTimeOffset PublishedAt { get; }

    public NationalProgramPublication(string programId, int version, string publisherSubjectId, DateTimeOffset publishedAt)
    {
        if (string.IsNullOrWhiteSpace(programId)) throw new ArgumentException("Program ID required.", nameof(programId));
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        if (string.IsNullOrWhiteSpace(publisherSubjectId)) throw new ArgumentException("Publisher subject required.", nameof(publisherSubjectId));
        if (publishedAt == default || publishedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Publication timestamp must be explicit UTC.", nameof(publishedAt));
        ProgramId = programId;
        Version = version;
        PublisherSubjectId = publisherSubjectId;
        PublishedAt = publishedAt;
    }
}

/// <summary>In-memory publication evidence keyed to exact version. Does not publish by itself.</summary>
public sealed class NationalProgramPublicationIndex
{
    private readonly Dictionary<(string, int), NationalProgramPublication> _published = new();

    public void Record(NationalProgramPublication publication, NationalProgramCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Find(publication.ProgramId, publication.Version) is null)
            throw new InvalidOperationException("Cannot record publication of an unknown national version.");
        if (!_published.TryAdd((publication.ProgramId, publication.Version), publication))
            throw new InvalidOperationException("Publication already recorded.");
    }

    public bool IsPublished(string programId, int version) =>
        !string.IsNullOrWhiteSpace(programId) && version > 0 && _published.ContainsKey((programId, version));
}
