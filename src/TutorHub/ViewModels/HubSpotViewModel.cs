namespace TutorHub.ViewModels;

public record HubSpotViewModel(
    bool Connected,
    long? PortalId,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? LastSyncAt,
    string? LastError,
    int Students,
    int SyncedStudents,
    int CompletedLessons,
    int SyncedLessons,
    bool BackgroundProcessing);
