# 3. What e-books may be uploaded and shared

- **Status:** Accepted
- **Date:** 2026-11-21
- **Issue:** #80 (part of epic #78, needed before slice 4, #87)

## Context

Epic #78 lets users upload their own e-books (EPUB files). An uploaded e-book can be:

- **Private:** only the owner reads it.
- **Shared with friends:** up to 10 friends can read it (ADR 0005).
- **Offered to the shared library:** once an admin approves it, everyone can read it, and it stays there even if the owner later deletes it (#78).

Unlike a physical book, an e-book file can be copied without limit. Letting other people read a file someone uploaded comes close to distributing it, and most e-books are protected by copyright. NyriaNest needs rules for which e-books may be uploaded and shared, and a way to make those rules hold that fits a small app without lawyers or automatic copyright checks.

Two decisions already made in #78 and ADR 0005 limit how far a file can spread:

- **Only the owner can download.** Friends and shared-library readers read in the app (`CanDownload`, #87; the in-app reader, #27). No copy leaves the app.
- **E-book sharing with friends is limited to 10 friends** (ADR 0005).

## Options

1. **No file sharing at all.** E-books stay private; only the owner reads them. Safest, but it removes the point of sharing e-books with friends and of a shared library.
2. **Only links to legal sources** (for example Project Gutenberg) instead of files. No files to worry about, but nothing can be read in the app, and a user's own e-books can't be shared at all.
3. **The same strict rules everywhere:** only public domain, openly licensed or the uploader's own work, for private and friends' sharing too. Safe, but it stops users from keeping and sharing the e-books they actually own, and nobody could check it for private books anyway.
4. **Rules that follow how far the e-book reaches:** free for the owner and their friends, strict for the shared library, which is checked by an admin.

Also considered, on top of option 4: **a confirmation box** when offering an e-book to the shared library ("I have the right to share this"). Left out, see Consequences.

## Decision

Option 4.

**Private and shared with friends: any e-book the user owns.** Users aren't restricted in what they upload for themselves or share with friends. It stays defensible because of how far it can reach: friends only read it in the app and can't download it, and at most 10 friends can read a user's e-books. That is close to lending a book to a few friends, not to spreading copies. E-book licences, for example for books bought in a store, often don't allow even this kind of sharing. NyriaNest accepts that knowingly and doesn't check it: it is a deliberate trade-off, not a claim that every case is allowed.

**The shared library: only e-books that may be shared with everyone:**

- **public domain** (the copyright has run out),
- **DRM-free with a licence that allows sharing** (for example Creative Commons),
- **the uploader's own work.**

**How it's enforced:**

- **Only the owner can download** (`CanDownload`, #87). Friends and shared-library readers read in the app.
- **The 10-friend limit** keeps friends' sharing small (ADR 0005).
- **Admin approval is the check for the shared library.** An offered e-book waits as Pending until an admin approves both the file and the book's details (#88, #89). The admin checks it against the rules above and rejects it if it doesn't qualify or can't be shown to. E-books an admin adds straight to the shared library follow the same rules.

**When a file turns out not to be allowed**, an admin removes it, from the shared library or from a user's own e-books, and the owner is told why through the notification step in #75.

## Consequences

- Upload in My library (#87) has no content check. Private and friends' e-books are the owner's responsibility.
- Approving an e-book into the shared library (#88, #89) is where these rules are applied. The admin needs to see enough to judge it: the file, the details and who offered it.
- Removing a book that isn't allowed reuses the admin's existing removal with a reason (#83) and the notification to the owner (#75), so it needs no new flow.
- This decision depends on reading in the app. Until the reader (#27) is done, `Ebooks:ReadersCanDownload` lets readers download in development only; it must stay off elsewhere. If friends or shared-library readers could ever download, this ADR would need to be looked at again.
- The uploader isn't asked to confirm they have the right to share an e-book when offering it. Admin approval is the only check, so a confirmation box would add a step without changing who decides. If approval ever goes away or becomes automatic, the box should be reconsidered.
