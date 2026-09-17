# Local database

A runnable SQL Server for development, and the groundwork for automated tests that need
real data rather than fixtures.

## Why a container

The application's Local mode is configured for SQL Server **LocalDB**
(`(LocalDb)\MSSQLLocalDB`), which only exists on Windows. This container is the
cross-platform stand-in: the same engine over TCP, so one connection string works from
Windows, Linux and CI.

## Start it

    docker compose -f db/docker-compose.yml up -d
    ./db/wait-for-db.sh

Listening on `127.0.0.1:14330` - deliberately not 1433, so it cannot collide with a real
SQL Server on the machine. The SA password is a local development secret and is committed
on purpose; the container holds no production data and is bound to loopback.

## Give the application the database

The connection string is hard-coded per mode in `Shared/PlasmaShared/GlobalConst.cs`.
`ZK_CONNECTION_STRING` overrides it, and is read after the mode is applied:

    export ZK_CONNECTION_STRING="$(./db/connection-string.sh)"

## Two ways to get a schema

**From the migrations, no dump needed.** Local mode sets `AutoMigrateDatabase = true`, so
`ZkDataContext` runs `Database.CreateIfNotExists()` and applies all 117 EF6 migrations on
first use, then `Migrations/Configuration.Seed` adds forum categories, a resource row and,
because the mode is Local, `LocalSeed`. That gives a working empty site with no personal
data in it at all.

The catch is that EF6 migrations run on .NET Framework, so this path needs Windows or a
Windows CI runner. It cannot be driven from Linux today - which is one more thing the
EF Core migration would fix, since `dotnet ef` is cross-platform
(`ZkData/EFCORE-MIGRATION.md`).

**From a dump**, when you need real data to test against: see `db/dumps/README.md`.

## Stop it, and start over

    docker compose -f db/docker-compose.yml down        # keeps the data
    docker compose -f db/docker-compose.yml down -v     # deletes it

## Towards automated tests

`Tests.Portable` deliberately has no database: it links pure source files and runs on
.NET 9 in about 150 ms, and that should stay true. Database-backed tests want to be a
separate project so a developer without Docker can still run the fast suite.

When that project is written, the shape that fits what is here: bring the container up in
CI as a service, restore a small redacted dump, point `ZK_CONNECTION_STRING` at it, and
let each test run in a transaction that is rolled back. Note the blocker above - until
EF6 is migrated, schema creation needs a Windows runner, so the first database tests will
either run on the existing self-hosted Windows runner or restore a dump that already has
the schema in it.
