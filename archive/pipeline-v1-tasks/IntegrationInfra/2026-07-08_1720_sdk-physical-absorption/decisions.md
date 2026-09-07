# Decisions

- Physical absorption = one repo, TWO packages, two version lines (Infra 1.x-preview, SDK 3.x). Never merge the SDK types into the Infra assembly.
- Plain copy + provenance note, NOT git-filter-repo history extraction. — history stays reachable in ISB; matches the repo's carry discipline.
- Samples stays in ISB. — its only consumer is ISB's API.UnitTests; moving it breaks that for zero gain.
- ISB untouched this task; the host ProjectReference→PackageReference swap is step S4 of the absorption sequence, coordinated with ISB owners.
- SDK version carried as-is at 3.2.1 (one patch ahead of the newest published 3.2.0 — publishing it is the pipeline task's job, not this one's).
- Adapter-cosplay versioning (`AdapterVersion`/`IsAdapter=true`) is dropped at the door; the carried packaging declares only what a contracts package needs.
- SDK-dir Directory.Build.props: executor decides parent-import vs standalone and records why (standalone is acceptable — the parent's only load-bearing content is the 1.x Version the SDK must NOT inherit; verify nothing else in the parent matters to the SDK).
- The carried SDK test project aligns to Infra's central package pins (test csproj edits allowed; SDK product code verbatim).
- Namespace convergence: deferred indefinitely per operator ("we'll worry about namespaces if it makes sense").
