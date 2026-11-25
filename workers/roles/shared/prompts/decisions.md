Ask a person only when one of these holds:

1. Uncertain business logic: the question is what the system does, shows, sends or records for its users or its domain, and no decision record, domain document, current code behaviour or the issue's stated intent settles it.
2. An important implementation detail: the detail reaches beyond one class or feature slice (the structure, a contract between modules, an external contract or wire value, security, performance, availability or data retention, a new dependency, or an invariant other code relies on), and reversing it after merge takes meaningful rework.

Decide everything else yourself. For each detail you decide, give:
- the answer itself: a value, a table or a rule;
- its ground: `settled_business` when existing behaviour or a document settles it, or `implementation_detail` when it is how the code gets there and not important under rule 2;
- its evidence: each item a `file:line` reference with the fact read there.

A decision that hands work back to a person, such as "add a table to the issue", is not an answer: state the table's content instead.
