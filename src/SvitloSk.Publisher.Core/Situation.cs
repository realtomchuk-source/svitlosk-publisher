namespace SvitloSk.Publisher.Core;

public enum Situation
{
    MorningStartup = 1,
    ChangedAddresses = 2,
    RemovedAddresses = 3,
    AddedAddresses = 4,
    TerritoryAppeared = 5,
    TerritoryDisappeared = 6,
    TomorrowForecastAppeared = 7,
    TomorrowForecastDisappeared = 8,
    GraphicScheduleChanged = 9,
    TechnicalInfoExpired = 10,
    CleanupStarted = 11,
    EditionClosing = 12,
    NoChangesDetected = 13,
    ExternalProducerUnavailable = 14,
    GraphicUnavailable = 15,
    CommentFlood = 16,
    UnexpectedInconsistency = 17
}
