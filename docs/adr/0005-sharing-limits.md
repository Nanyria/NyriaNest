# 5. The sharing limits (100 / 10)

- **Status:** Accepted
- **Date:** 2026-11-21
- **Issue:** #82 (part of epic #78, needed before slice 2, #84)

## Context

ADR 0004 gives every user their own library of physical books and e-books, and lets them share it with friends. Slice 2 (#84) adds the friends: a user sends a request by user name, the other user accepts, and then each of them chooses, per friend, whether to share their physical library and whether to share their e-books. Sharing goes one way: I can share with Anna without Anna sharing with me.

The two kinds of sharing don't give the friend the same thing:

- **Sharing the physical library** shows the friend a list of my books and lets them ask to borrow one. The book itself stays with me until I hand it over, and each copy can only be lent to one person at a time. What a friend gets is a look at my shelf.
- **Sharing my e-books** lets the friend read them in the app whenever they like, without asking me. They can't download the files: only the owner can (`CanDownload`, #87). No copy leaves the app, which is what makes it sharing with friends and not publishing. But unlike a physical copy, an e-book can be read by every friend I share it with at the same time.

Reading in the app keeps the files from spreading, but not the number of readers: without a limit, a user could accept requests from strangers and let thousands of people read their e-books. That is no longer a circle of friends. Which e-books may be uploaded and shared at all is decided separately in ADR 0003 (#80, planned); a limit on e-book sharing keeps friends' sharing at the size of a circle of friends, whatever ADR 0003 ends up allowing.

## Options

1. **No limits.** Simplest, but nothing stops one e-book from being read by any number of people.
2. **One limit on the number of friends.** Easy to explain, but it limits the harmless part (seeing a shelf) as hard as the part that lets many people read the same e-book, or lets the e-books reach too many readers.
3. **One limit shared by both kinds of sharing.** Same problem: it has to be either too tight for the shelf or too loose for the e-books.
4. **A separate limit for each kind of sharing, counted on the sharing settings.** Friendships are not limited; what is limited is how many friends I share each thing with.

## Decision

Option 4.

- A user can share their **physical library with at most 100** friends and their **e-books with at most 10**.
- The limits count the sharing settings that are on, per friend and per direction, not the friendships. I can have more than 100 friends; I just can't share my shelf with more than 100 of them. What friends share with me doesn't count towards my limits.
- Both limits are **configuration values**, so they can be changed without a code change. 100 and 10 are the defaults.

**Why they differ.** Sharing the physical library only shows a list, and a copy can only be with one person at a time, so the limit is there for privacy and to keep the list of people manageable; 100 is generous. Sharing e-books gives no one a copy (friends read in the app, only the owner can download), but every friend can read the same e-book at once, as if one copy were lent to all of them together. The limit keeps that to a small circle; 10 is about the size of a group of close friends or a family.

**At the limit.** Turning on a setting beyond the limit (the 101st physical, the 11th e-book) is refused with a message that says which limit is reached. To share with someone new, the user first turns the setting off for someone else. Nothing is turned off automatically.

**Not passed on.** Sharing only reaches the friend it is turned on for. Friends of my friends see nothing of mine, even if my friend shares their own library with them.

## Consequences

- Each friendship stores the two sharing settings per direction (#84). The limits are checked when a setting is turned on, by counting the user's own settings that are on.
- Turning a setting off, or removing a friend (which turns both directions' settings off), frees a place straight away.
- The Friends page shows how much of each limit is used ("7 of 10"), so the user knows before reaching it (#84).
- If the limits are lowered in configuration, users who are already above the new limit keep what they have; they just can't turn on more until they are under it again. Nothing is turned off behind their back.
- The limit of 10 assumes friends can only read shared e-books in the app. Download stays with the owner (`CanDownload`, #87); `Ebooks:ReadersCanDownload` lets readers download in development only, until the in-app reader (#27) is done. If friends could ever download, this limit would need to be looked at again.
- The limits don't replace ADR 0003: which e-books may be shared at all is decided there. This ADR only limits how far a user's own e-books can be shared.
