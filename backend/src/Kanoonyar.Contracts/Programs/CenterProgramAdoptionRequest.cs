namespace Kanoonyar.Contracts.Programs;

/// <summary>Explicit planning request binding the center, actor and national version.
/// No publication, authorization or acceptance is implied by construction.</summary>
public sealed record CenterProgramAdoptionRequest
{
    public string CenterId { get; }
    public string ActorSubjectId { get; }
    public string ProgramId { get; }
    public int NationalVersion { get; }
    public string LocalTitle { get; }

    public CenterProgramAdoptionRequest(string centerId, string actorSubjectId,
        string programId, int nationalVersion, string localTitle)
    {
        if (string.IsNullOrWhiteSpace(centerId)) throw new ArgumentException("Center ID is required.", nameof(centerId));
        if (string.IsNullOrWhiteSpace(actorSubjectId)) throw new ArgumentException("Actor is required.", nameof(actorSubjectId));
        if (string.IsNullOrWhiteSpace(programId)) throw new ArgumentException("Program ID is required.", nameof(programId));
        if (nationalVersion <= 0) throw new ArgumentOutOfRangeException(nameof(nationalVersion));
        if (string.IsNullOrWhiteSpace(localTitle)) throw new ArgumentException("Local title is required.", nameof(localTitle));
        CenterId = centerId;
        ActorSubjectId = actorSubjectId;
        ProgramId = programId;
        NationalVersion = nationalVersion;
        LocalTitle = localTitle;
    }
}
