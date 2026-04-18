namespace users_api.Events;

public record UserCreatedEventV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid UserId,
    string Email,
    string DisplayName,
    int SchemaVersion = 1
);
