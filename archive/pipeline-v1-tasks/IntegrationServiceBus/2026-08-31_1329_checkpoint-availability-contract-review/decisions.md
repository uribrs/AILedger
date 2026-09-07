# Decisions

* Produce a plausibility verdict, not an implementation plan disguised as certainty.
* Use one repository-focused worker per repository, then synthesize centrally.
* Treat checkpoint presence, checkpoint eligibility, and caller intent as separate concepts until the code proves they can be collapsed.
* Proceeding on unverified: existing request identifiers are sufficient for the new endpoint. If wrong: the API contract requires a cross-repo identifier change.
* Do not accept current cymulate-integrations branch ancestry as an implementation base.
