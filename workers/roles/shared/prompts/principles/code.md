Code:
- A bug fix removes the root cause, not the symptom.
- Preconditions are checked first and exit early, before any state changes; the happy path is the least indented.
- Warnings are fixed at their cause; an unavoidable suppression is the narrowest available and states why.
- A new dependency is checked against its registry for name, current stable version and licence before use.
- A comment gives only a reason the code cannot show, such as a business rule or a non-obvious constraint; code that is clear on its own has no comment.
- A comment's reason stands on its own; it may cite a standard, a URL or a code symbol, never an issue, pull request or review.
- A doc comment follows the same rules and is left out when it repeats the signature; test names and descriptions state behaviour and never cite an issue, pull request or acceptance criterion.
- A comment on code the change edits is kept only if it still gives a reason; otherwise it is removed.
- Comment patterns the repository uses throughout, such as Arrange/Act/Assert markers or licence headers, stay.
