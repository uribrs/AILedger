- annotate: response-level map field -> scalar const (string|number|bool); injected
  first-position post-mapping/post-conversion; collision drops the source property
  case-insensitively; non-object records pass through; applies to classic ops,
  workflow stages, merge sources, streamed path.
- const mapping value: mapping value form { const: <scalar> } — literal emitted as-is;
  composes with $self ({type: {const: machine}, data: {source: $self}}).
- Offline oracle = inversion round-trip: native capture record -> strip label/unwrap ->
  engine transform -> must equal native record (canonical compare), all 6 lanes sampled
  + full pass where cheap.
- Stage set: ONE native findings sequence (machines, vulnerabilities,
  vulnerability_changes, recommendations, software).
- defender yaml branch in magic-integration: branch off feature/qualys-ivmc-real-vendor-yamls.
