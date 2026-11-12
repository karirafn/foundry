Domain modelling (where the code models a business domain):
- Code uses the domain's terms exactly (DOMAIN.md when present).
- Logic lives with the data it operates on: entity, then value object; a domain service only for logic spanning aggregates.
- Value objects validate on creation and entities guard their own invariants, so no caller can bypass a rule.
- A lifecycle is one type per state; transitions return the next state.
- Data with no invariants or transitions stays a plain record.
