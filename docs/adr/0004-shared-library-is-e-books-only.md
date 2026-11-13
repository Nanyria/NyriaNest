# 4. The shared library holds e-books only

- **Status:** Accepted
- **Date:** 2026-11-13
- **Issue:** #81 (part of epic #78)

## Context

Today NyriaNest is one library that lends physical books. Admins add the books, every user borrows from the same collection, and the existing loan and reservation system (`CheckedOutItem`, `ReservationItem`, the queue, the expected dates and the status history) tracks who has each copy. A book can also have an e-book file, but downloading it requires a loan.

Epic #78 splits the app into two parts: a shared library everyone can read from, and each user's own library ("My library") of physical books and e-books that they can share with friends. That raises the question of what the shared library should hold.

A physical copy is always in one person's home. Lending it means handing it over and getting it back, which only works between the person who has the book and the person who borrows it. A shared library of physical books needs someone who holds the books and handles the hand-overs. In today's model that is the admins, but NyriaNest has no physical library building or staff behind it, so nobody really has that role.

## Options

1. **The shared library holds e-books only.** Physical books always belong to a user, live in that user's library and are lent by their owner, to friends through the loan and reservation system or to anyone else by name.
2. **Keep a shared library with physical books kept by admins** (today's model), next to the users' own libraries.

## Decision

Option 1. An e-book can be read by many people without anyone handing anything over, so it fits a shared library. A physical book does not: the person who has it is the only one who can lend it, so the lending belongs between the owner and the borrower. Option 2 would keep admins as the holders of books they don't have, and would need two kinds of physical lending (from the shared library and from users) with different rules.

## Consequences

- `Book` gets an `OwnerId`: the user whose book it is, or null for the shared library. Physical books are never in the shared library, so they always have an owner.
- Today's physical books are removed in slice 1 (#83), together with their loans, reservations and history. The seeder is rewritten for the new model.
- Admins don't take part in lending, but keep full access to every library, including loans, reservations, requests and history, so they can help when something goes wrong (#50, #63).
- One Library page shows everything available to the user: the shared library, what friends share with them and their own books. Each book has an owner tag, and a "From" filter picks where the books come from, so there is no separate page per friend (#45).
- Which e-books may be uploaded to the shared library is decided separately in ADR 0003 (#80), and the sharing limits for friends in ADR 0005 (#82).
- A shared library of physical books could come back later if NyriaNest is ever used by a real library with staff who hold the books. That would be a new kind of owner, not a change to this decision.
