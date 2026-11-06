# 1. `User` stays a Domain entity that inherits `IdentityUser`

- **Status:** Accepted
- **Date:** 2026-11-06
- **Issue:** #47 (Clean Architecture restructure)

## Context

In Clean Architecture the Domain project ideally has no dependencies at all. NyriaNest's `User` entity inherits ASP.NET Core Identity's `IdentityUser`, which brings login fields (password hash, security stamp, lockout) into the entity.

`User` is also a real domain entity. Loans, reservations, reviews, ratings, favourites, notifications and status history all point at it, and the database maps it to the `AspNetUsers` table together with the profile fields (name, bio, profile picture).

## Options

1. **Keep `User : IdentityUser` in Domain.** Domain then references one small package, `Microsoft.Extensions.Identity.Stores`, which defines `IdentityUser`. That package has no EF Core or ASP.NET in it.
2. **Split it:** a Domain `Member` entity with the profile and the relations, and an `ApplicationUser : IdentityUser` in Infrastructure that only handles login, linked by id.

## Decision

Option 1. The restructure in #47 had to leave behaviour and the database unchanged. Option 2 changes the schema (a new table or a split of `AspNetUsers`), needs a data migration for existing users and rewires every relation that points at `User`. That's a lot of risk for a purity gain the app doesn't need yet.

## Consequences

- Domain has exactly one package reference: `Microsoft.Extensions.Identity.Stores`. Nothing else outside .NET itself.
- The login fields are visible on the entity. The API never returns entities, only DTOs (`UserDto` has no password hash or security stamp), and the tests check this.
- The split stays possible later and is tracked in #55. It becomes worth doing if Domain must be completely package-free, or if members and login accounts start to differ (members without a login, or several logins per member).
