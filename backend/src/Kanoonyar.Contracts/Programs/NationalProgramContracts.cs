namespace Kanoonyar.Contracts.Programs;

/// <summary>Headquarters-owned, versioned national program definition.
/// A local center cannot modify the national objectives or requirements.</summary>
public sealed record NationalProgramVersion
{
    public string ProgramId { get; }
    public int Version { get; }
    public string Title { get; }
    public IReadOnlyList<string> NationalObjectives { get; }
    public IReadOnlyList<string> MandatoryRequirements { get; }

    public NationalProgramVersion(string programId, int version, string title,
        IEnumerable<string> nationalObjectives, IEnumerable<string> mandatoryRequirements)
    {
        if (string.IsNullOrWhiteSpace(programId)) throw new ArgumentException("Program ID is required.", nameof(programId));
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title is required.", nameof(title));
        ArgumentNullException.ThrowIfNull(nationalObjectives);
        ArgumentNullException.ThrowIfNull(mandatoryRequirements);
        var objectives = nationalObjectives.ToArray();
        var requirements = mandatoryRequirements.ToArray();
        if (objectives.Any(string.IsNullOrWhiteSpace) || objectives.Distinct(StringComparer.Ordinal).Count() != objectives.Length)
            throw new ArgumentException("Objectives must be nonblank and unique.", nameof(nationalObjectives));
        if (requirements.Any(string.IsNullOrWhiteSpace) || requirements.Distinct(StringComparer.Ordinal).Count() != requirements.Length)
            throw new ArgumentException("Requirements must be nonblank and unique.", nameof(mandatoryRequirements));
        ProgramId = programId;
        Version = version;
        Title = title;
        NationalObjectives = Array.AsReadOnly(objectives);
        MandatoryRequirements = Array.AsReadOnly(requirements);
    }
}

/// <summary>Center-owned local planning data pinned to one national version.
/// This is a contract, not an approval, assignment or authorization decision.</summary>
public sealed record CenterProgramPlan
{
    public string CenterId { get; }
    public string ProgramId { get; }
    public int NationalVersion { get; }
    public string LocalTitle { get; }
    public IReadOnlyList<string> LocalActivities { get; }

    public CenterProgramPlan(string centerId, NationalProgramVersion national, string localTitle,
        IEnumerable<string> localActivities)
    {
        if (string.IsNullOrWhiteSpace(centerId)) throw new ArgumentException("Center ID is required.", nameof(centerId));
        ArgumentNullException.ThrowIfNull(national);
        if (string.IsNullOrWhiteSpace(localTitle)) throw new ArgumentException("Local title is required.", nameof(localTitle));
        ArgumentNullException.ThrowIfNull(localActivities);
        var activities = localActivities.ToArray();
        if (activities.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Activities must be nonblank.", nameof(localActivities));
        CenterId = centerId;
        ProgramId = national.ProgramId;
        NationalVersion = national.Version;
        LocalTitle = localTitle;
        LocalActivities = Array.AsReadOnly(activities);
    }
}
