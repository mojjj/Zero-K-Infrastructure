# Migrating ZkData from Entity Framework 6 to EF Core

EF6 does not run on .NET 9. It is the first blocker in the port: everything else in
Phase 3 waits behind it, because `ZkData` is referenced by the website, the lobby server,
`Fixer` and `AutoRegistrator`.

This file is the measured shape of that job. Numbers are counts from the tree, not
estimates, so they can be re-measured with the commands given.

## Size of the problem

| | Count | Command |
|---|---|---|
| `System.Data.Entity` references | 390 | `grep -rn 'System.Data.Entity' --include=*.cs Shared ZkData Zero-K.info ZkLobbyServer` |
| Entity classes | 83 | `ls ZkData/Ef/*.cs` |
| Versioned migrations | 117 | `ls ZkData/Migrations/*.cs \| grep -v Designer \| grep -v Configuration` |
| `virtual` navigation properties (lazy loading) | 154 | `grep -rhoE 'public virtual (ICollection\|[A-Z][A-Za-z]*) ' ZkData/Ef` |
| Fluent relationship config lines | 144 | in `ZkData/ZkDataContext.cs` `OnModelCreating` |

## What has to change, in the order it bites

### 1. The migration history has no EF Core equivalent

117 EF6 migrations plus a `DbMigrationsConfiguration`, applied at startup by

    Database.SetInitializer(new MigrateDatabaseToLatestVersion<ZkDataContext, Configuration>());  // ZkDataContext.cs:811

EF Core cannot read EF6's `__MigrationHistory` table. The realistic route is to **stop
replaying history**: baseline the current schema as a single EF Core initial migration,
and mark it as already applied on the live database rather than running it. The 117 EF6
migrations stay in the repository as the record of how the schema got here, and stop
being executable. Nothing about this can be validated without a database dump, which the
repository does not contain - see `Zero-K.info/HOSTING.md`.

### 2. The fluent configuration is written in EF6's vocabulary

`OnModelCreating` in `ZkDataContext.cs`:

| EF6 | Occurrences | EF Core |
|---|---|---|
| `HasMany` | 111 | same name, different builder shape |
| `WithRequired` | 73 | `WithOne` + `IsRequired` |
| `WillCascadeOnDelete` | 77 | `OnDelete(DeleteBehavior.*)` |
| `WithOptional` | 34 | `WithOne` with a nullable FK |
| `WithMany` | 8 | same |
| `HasRequired` | 3 | `HasOne` + `IsRequired` |
| `HasOptional` | 2 | `HasOne` with a nullable FK |
| `HasKey` | 2 | same |

Mechanical but not automatic: EF Core's default delete behaviour differs from EF6's, so
each of the 77 `WillCascadeOnDelete` calls has to be read rather than pattern-replaced.

### 3. Lazy loading is on by default in EF6 and absent in EF Core

154 `virtual` navigation properties currently lazy-load. EF Core needs
`Microsoft.EntityFrameworkCore.Proxies` and an explicit `UseLazyLoadingProxies()`, or the
call sites need `Include()`. Keeping proxies is the smaller change and preserves
behaviour; it is also the option that hides N+1 queries, which is what the current code
relies on.

### 4. APIs with no direct successor

- **`SqlFunctions.PatIndex`** - 4 uses, all substring search:
  `Zero-K.info/AppCode/ContentServiceImplementation.cs:91`,
  `Zero-K.info/Controllers/ClansController.cs:245` and `:345`,
  `ZkLobbyServer/autohost/MapPicker.cs:165`.
  EF Core's nearest equivalent is `EF.Functions.Like`, which is not the same predicate.
  `PATINDEX` returns a position and these compare it to `> 0`, so a `LIKE '%term%'`
  translation is behaviour-preserving here - but it must be done at the switch, because
  `EF.Functions` does not exist in EF6.
- **`Database.ExecuteSqlCommand`** - 4 uses, renamed to `ExecuteSqlRaw`:
  `ZkData/Migrations/Configuration.cs:45`, `Zero-K.info/AppCode/ResourceLinkProvider.cs:57`
  and `:59`, `ZkLobbyServer/ZkServerTraceListener.cs:22`.

## Already done

These were cleared ahead of the migration because they are safe on EF6 today:

- **`Microsoft.Linq.Translations` removed.** A third-party EF6-only library with no EF
  Core support. It turned out to be entirely unused: seven stale `using` directives and
  three `PackageReference` entries, with its only two `CompiledExpression` uses inside a
  commented-out block in `Ef/Account.cs`. That block is left as it is - it is a comment,
  and it records what `EffectiveElo` used to be.
- **`IDbSet<T>` replaced with `DbSet<T>`** in the four extension methods in
  `ZkData/DbExtensions.cs`. EF Core has no `IDbSet<T>`; `DbSet<T>` exists in both, so
  these signatures now port unchanged.

## What is not a problem

Worth recording, because these are the usual EF6 migration blockers and this codebase
does not have them:

- No `ObjectContext`, and no `IObjectContextAdapter`.
- No `DbGeography` or other spatial types.
- No `MapToStoredProcedures`.
- No EDMX - the model is code-first.
- No `DbFunctions` / canonical function calls.

## Order of work

1. Baseline the schema as one EF Core initial migration; agree how the live database gets
   marked as already at that baseline.
2. Rewrite `OnModelCreating`, reading each `WillCascadeOnDelete`.
3. Switch the package references, add lazy-loading proxies, fix `ExecuteSqlRaw` and the
   four `PatIndex` predicates.
4. Only then does `ZkData` target .NET 9, which is what unblocks the rest of Phase 3.

Steps 1 and 2 cannot be verified without a database. Step 3 is compile-checkable.
