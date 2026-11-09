Engineering principles. The repository's own style, testing and design conventions win where they differ; the safety rules above always apply.

Testing (where a test runner exists):
- Each behaviour change is driven by a test written first and seen failing for the expected reason.
- Production code is the minimum that passes its tests, refactored while they stay green.
- A bug fix starts with a test that reproduces it.
- Each test verifies one behaviour, with state isolated from other tests.
- A change that turns an existing test red is wrong until shown otherwise.

Design:
- Invalid states are unrepresentable: domain concepts have their own types, and required data is required by the type.
- Errors surface as early as possible: compile time over runtime, runtime over never.
- Defaults are safe and valid; the correct path takes the least code.
- Input is validated at the boundary and rejected with a clear message.
- Access is as restrictive as callers allow; members exist for production callers, not tests.
- Comments explain only why.

Domain modelling (where the code models a business domain):
- Code uses the domain's terms exactly (DOMAIN.md when present).
- Logic lives with the data it operates on: entity, then value object; a domain service only for logic spanning aggregates.
- Value objects validate on creation and own their concept's behaviour.
- A lifecycle is one type per state; transitions return the next state.
