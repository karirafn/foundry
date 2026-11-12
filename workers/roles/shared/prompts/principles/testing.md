Testing (where a test runner exists):
- Each behaviour change is driven by a test written first and seen failing for the expected reason.
- Production code is the minimum that passes its tests, refactored while they stay green; the tests cover every case the change must handle, errors included.
- A bug fix starts with a test that reproduces it.
- Each test verifies one behaviour, with state isolated from other tests.
- Assertions check values the code under test produced, never values the test supplied.
- A change that turns an existing test red is wrong until shown otherwise.
- A value only an external system produces (a status, an enum value, an error code, an id format) is copied into a test from a captured real response, and the test names where it was captured; it is never written from memory.
