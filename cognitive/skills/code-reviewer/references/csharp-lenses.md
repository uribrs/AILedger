# C# / .NET Review Lenses

Load this file when the stack under review is C# / .NET. Apply these lenses in addition to the general lenses in `../SKILL.md`.

## Language Pitfalls

Check:

- `ConfigureAwait` usage
- `CancellationToken` flow
- `IAsyncDisposable` / `IDisposable`
- `HttpClient` lifetime
- `System.Text.Json` behavior
- LINQ materialization traps
- `Task.WhenAll` exception behavior
- `SemaphoreSlim` release safety
- `ConcurrentDictionary` vs locked dictionary

## Data Structure Idioms

Check:

- Use `Dictionary` / `HashSet` when lookup, uniqueness, or joins dominate.
- Use `List` when order, sequential iteration, or append-only collection dominates.
- Avoid repeated `.FirstOrDefault`, `.Any`, or `.Where` scans inside loops when indexing would be cheaper.
- Avoid materializing full collections when streaming is possible.
- Use batching when remote calls or writes dominate.
- Use bounded concurrency over unbounded `Task.WhenAll`.
- Watch for accidental O(n²) logic.
