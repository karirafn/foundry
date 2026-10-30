# Dedicated UsageSnapshotUpdated hub method, not a SystemNotification category

## Context

The system hub's generic `SystemNotificationReceived` renderer filters categories by a
deny-list and renders unrecognised categories into an alert banner. Delivering usage as a new
`SystemNotification` category would leak an empty banner into the UI on every push.

## Decision

Add a first-class hub method `UsageSnapshotUpdated` carrying the usage-snapshot DTO (or null
for "no data"). An `IUsageSnapshotBroadcaster` abstraction in the Credentials module is
implemented by `SignalRUsageSnapshotBroadcaster` in the WebApi host (which has
`InternalsVisibleTo` access), sending on `IHubContext<SystemNotificationHub>`.
The frontend `UsageService` subscribes to the dedicated method.

## Consequences

The usage payload bypasses the notification renderer entirely — no banner-leak risk, and the
payload shape is decoupled from the generic notification contract. One more hub method to keep
in sync between server broadcaster and client subscription.
