# Close Claim Supersession and Challenge Consequence

Two conceptual gaps remain between the AILedger 2.0 kernel and design dossier v2.1. Both concern
causal governance — the mechanism the whole ledger exists for.

1. Superseding a claim is unevidenced yet destructive, and records no pointer to what replaced it.
2. A supported challenge changes nothing. Challenge disposition is currently advisory.

Also remove two enum members that are read by guards but never written by any path.
