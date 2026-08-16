// Test classes run in parallel. Each ApiFactory owns an isolated SQLite database (see
// ApiFactory.Settings and FixtureIsolationTests), so one class's lockouts, approvals or
// preference changes cannot reach another's.
