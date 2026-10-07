# Experimental isolated Roslyn worker

This is a host-operated compatibility experiment, **not an installed provider backend**.
It uses a frozen, sanitized source snapshot. It neither follows live working-tree edits
nor provisions private-feed credentials. Do not point it at an ordinary checkout.
The governed trial and remaining limits are recorded in
[`docs/roslyn-container-trial.md`](../../docs/roslyn-container-trial.md).

## Build and prepare

The Dockerfile pins official .NET 10 SDK and .NET 8 framework-pack images by digest.
Put the already installed `roslyncodelens.mcp.2.18.1.nupkg` beside a copy of the
Dockerfile in a temporary build context. Build with `docker build --network none`.
No repository source or credentials belong in that context. Keep the resulting full
`sha256:` image ID from `docker image inspect`; the worker requires an immutable ID.

Run `prepare.py SOURCE DESTINATION` to copy tracked source/build files into a new
disposable directory. It excludes hidden state, symbolic links, bin/obj, configuration
credentials and most content files. This is deliberately not a complete repository
export; custom content/imports may need explicit preparation. Copying does not establish
platform equivalence or successful project loading.

Use a directory shared with the Docker VM. On this host Docker is Colima, and host
`/private/tmp` is not shared. The trial used ignored `artifacts/roslyn-container-probe`.
Avoid putting copied projects under an actual project's source glob.

Before navigation, restore the disposable copy in an isolated container, mounting it
at `/workspace` and the approved package-cache directory read-only at `/packages`.
Use the same pinned image, non-root UID/GID, no network, dropped capabilities,
`no-new-privileges`, a read-only root, resource limits and private writable `/tmp`.
The snapshot is writable **only during this explicit preparation**; never mount the
original checkout writable. For the trial:

```sh
dotnet restore /workspace/adapters/src/Cymulate.Integration.Adapters/Cymulate.Integration.Adapters.sln \
  --source /packages --ignore-failed-sources -p:NuGetAudit=false --disable-parallel
```

Missing packages are a preparation failure. Do not accept a Roslyn `ready` result as
proof that metadata references resolved. This experiment used only packages already
cached on this host and made no private-feed request.

## Run

From the trusted host, outside the provider process:

```sh
python3 scripts/roslyn-container-probe/worker.py \
  --snapshot /absolute/disposable/sources \
  --packages /absolute/approved/package-cache \
  --output /absolute/new/worker-output \
  --image sha256:FULL_IMAGE_ID
```

The worker mounts both inputs read-only, copies sources into private tmpfs, and starts
Roslyn as a non-root container process with no network or Docker socket. Its local
authenticated endpoint forwards only the navigation allowlist. The stdio client path
is in `ready.json`; its file contains an ephemeral credential, so do not publish it.
Keep output outside the snapshot and outside provider writable grants.

For one fresh governed launch, the host can set `AILEDGER_ROSLYN_EXECUTABLE` to this
client path. AILedger's existing navigation bridge still validates solution selection,
filters tools, records bounded failures and protects the client directory. The provider
does not start Docker or acquire Docker API access. The prototype permits one MCP client
session per worker; use a new worker for each new launch.

Stop the host worker with SIGTERM after the launch finishes. Check `cleanup.json` for
both `containerRemoved` and `snapshotUnchanged`; a failed Docker query is not proof of
removal. A stalled worker gets five seconds for EOF shutdown, then the host removes only
its exact named container. Diagnostic logs remain under the output directory.

## Checks and limits

```sh
PYTHONPYCACHEPREFIX=/tmp/ailedger-python-cache python3 -m unittest discover \
  -s scripts/roslyn-container-probe -p 'test_*.py'

python3 scripts/roslyn-container-probe/smoke.py \
  --image sha256:FULL_IMAGE_ID --packages /absolute/approved/package-cache \
  --shared-parent /absolute/shared/disposable-parent --output /absolute/new/smoke-output
```

The smoke test uses actual Roslyn definitions/references, refuses stale snapshots,
observes an edit in a fresh worker and forcibly cleans up a paused container. It uses no
model. Snapshot refresh is explicit restart, not live synchronization. The source-only
copy, host-specific Python/Docker paths, diagnostic retention, image/package provisioning,
concurrent admission, robust path mapping, deadline handling and worker lifecycle all
need product integration before this can be advertised as a normal backend.
