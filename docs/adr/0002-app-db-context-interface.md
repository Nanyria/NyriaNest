# 2. The services use an `IAppDbContext` interface, not repositories for everything

- **Status:** Accepted
- **Date:** 2026-11-06
- **Issue:** #47 (Clean Architecture restructure)

## Context

Before #47, the services used `AppDbContext` directly in about 45 places, most of them in `UserActionsService`. `AppDbContext` belongs in Infrastructure, because it knows about SQL Server, the migrations and Identity's tables. The services belong in Application, which must not depend on Infrastructure.

Some queries already go through repositories (`IBookRepo`, `IUserRepo`). The rest query the tables directly, and adding an e-book uses a database transaction so a failed file upload rolls the new book back.

## Options

1. **An `IAppDbContext` interface in Application**, exposing the entity tables (`DbSet<T>`), `SaveChangesAsync` and `BeginTransactionAsync`. `AppDbContext` implements it in Infrastructure. The service code keeps its queries.
2. **Move every query behind a repository method**, so Application doesn't reference EF Core at all.

## Decision

Option 1. EF Core's `DbContext` already acts as a repository and a unit of work, and this is a common, well-known approach in .NET Clean Architecture templates. Option 2 would rewrite most of the service code inside a restructure that had to leave behaviour unchanged.

## Consequences

- Application references `Microsoft.EntityFrameworkCore`, but not the SQL Server provider. It can't see migrations, connection strings or Identity's tables.
- `IAppDbContext` is registered as the **same** scoped instance as `AppDbContext`, so the services and repositories share tracked entities and the e-book transaction covers both. A test guards this (`DependencyInjectionTests`).
- The services are tested through the API against an in-memory SQLite database rather than with mocked `DbSet`s, which tend to pass while real queries fail.
- Moving queries into repositories stays possible one service at a time, if a query becomes complex enough to deserve a name of its own.
