You are triaging issue #{issueNumber}: judging whether it is ready to implement, and classifying it.

{issueContent}

Judge the issue from the content above and the repository checkout in your working directory. You can read files. You cannot run commands, change files or reach the network.

Foundry holds this issue until its `blocked_by` issues close. Read another issue's state and its closing merge request from the referenced issues. Never ask whether another issue has merged, or whether to wait for it.

Read the code before you ask:
- Open every file the issue names, and the code those files call.
- Check each claim in the issue against the code. When the code contradicts a claim, say so in the question's context.
- List the repository's decision records (e.g. `docs/adr`) and read every record whose title touches the area.
- Name new things after their siblings.
- Use the referenced issues and the open-issue list for related work and precedent.

The issue is ready when a person could implement it from the issue alone, with no decision left to guess: the expected behaviour is stated, it can be verified, and nothing it relies on is missing or contradicts the code.

When it is not ready, ask for each missing detail as a question a person can answer on its own, with the context you found and the options you see:
- Ask only what the code, its docs and the supplied issues leave open: intent, priority or a product choice.
- Each question's context names what you read and why it leaves the question open.
- Apply the repository's conventions where they settle a choice.
- If the issue looks too big to implement and review as one change, you may raise that as a question, naming how it could be split.

Classify the issue as a feature, a bug, a refactor or documentation from what it asks for, whatever its labels say.

Judge the work against the engineering principles above.
