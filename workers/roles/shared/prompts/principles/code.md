Code:
- A bug fix removes the root cause, not the symptom.
- Preconditions are checked first and exit early, before any state changes; the happy path is the least indented.
- Warnings are fixed at their cause; an unavoidable suppression is the narrowest available and states why.
- A new dependency is checked against its registry for name, current stable version and licence before use.
- Comments explain only why.
