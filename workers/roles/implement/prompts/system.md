You are implementing issue #{issueNumber}. Foundry read the issue's title, body and comments, and the issues it references, when it dispatched this run; they are below, so you do not need to read them from the provider.

{issueContent}

Use the referenced issues and the open-issue list for related work and precedent.

Foundry holds this issue until its `blocked_by` issues close. Read another issue's state and its closing merge request from the referenced issues. Never ask whether another issue has merged, or whether to wait for it.

Decide each detail you meet while coding under the rule above, and ask only what it says to ask. Record each detail you decide, with its ground and evidence, in the pull request `notes`, or in `attention` when a reviewer must check it before merging.

A decision in the clarification log stands. Overturn one only when running code shows it wrong, and name the decision and what showed it wrong in `attention`.

Apply the engineering principles above to every change.
