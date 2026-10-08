# Host-owned HTTP services

`ailedger service start`, `service inspect`, and `service stop` manage a containerized HTTP mock
for a task. Confined workers consume the supplied endpoint. They still cannot listen or connect
to the Docker socket. The host operator owns builds, service lifecycle, and native reference
proofs that start their own servers.

This is a macOS/Colima host facility, using the existing local Unix Docker transport. Set
`DOCKER_HOST` to a local `unix://` socket if the default `~/.colima/default/docker.sock` differs.
The engine must already be running. No daemon start, image pull, arbitrary host process, or model
launch is implicit in these commands.

## Declare a service

Put `ailedger.services.json` at the checkout's Git root:

```json
{
  "schemaVersion": 1,
  "profiles": {
    "yaml-mock": {
      "image": "yaml-mock:local",
      "containerPort": 9921,
      "hostPort": 0,
      "readinessPath": "/health",
      "readinessStatus": 200,
      "startupSeconds": 60
    }
  }
}
```

The image must contain the built mock and its entrypoint. The mock listens on the container's
interface; the kernel publishes only `127.0.0.1` on the host. `hostPort: 0` allocates a free port;
an explicit port is optional. Choose an actual endpoint/status provided by the mock for readiness.
Redirects and proxy environment settings are not followed by readiness probes.

Image tags are resolved to local immutable image IDs before confirmation. The preview digest
includes task/service identity, checkout path, Git HEAD, observed worktree fingerprint, complete
profile, resolved image ID and Docker socket. Any change requires a fresh preview. The source
fingerprint is an observation of the checkout, **not evidence that the image was built from it**.
Retain build evidence separately.

## Build and run

Use the existing `ailedger verification run` to record builds or complete host proofs. For example,
a repository's `ailedger.verification.json` may declare:

```json
{
  "schemaVersion": 1,
  "profiles": {
    "build-yaml-mock": {
      "command": "docker build --iidfile \"$AILEDGER_VERIFICATION_OUTPUT/image-id.txt\" -t yaml-mock:local -f Tools/YamlVendorMock/Dockerfile .",
      "requires": ["docker"],
      "resultFiles": ["*.txt"],
      "timeoutSeconds": 1800
    }
  }
}
```

Adapt the Dockerfile path to the repository. Verification already retains stdout/stderr, command,
exit status, result hashes, and before/after source fingerprints. It runs the selected command;
it does not infer that a successful build or simulation discharges a task's proof obligations.

Preview the service, read the complete plan, then use the returned confirmation:

```sh
ailedger service start --task TASK --actor operator --id mock-v1 \
  --checkout /absolute/checkout --profile yaml-mock
ailedger service start --task TASK --actor operator --id mock-v1 \
  --checkout /absolute/checkout --profile yaml-mock --confirm RETURNED_SHA256
ailedger service inspect --task TASK --actor operator --id mock-v1
ailedger service stop --task TASK --actor operator --id mock-v1
```

A service survives the launching CLI process and remains until stopped. Stop it before task
closeout. When mock sources change, retain the build evidence, stop the old service and start a
new service ID. Old service IDs are never reused. There is no adoption of arbitrary existing
containers, and no implicit restart/rebuild during a worker run.

`context build` and dispatched non-reviewer briefs include endpoints only after a successful start
observation has been recorded in the ledger. These are dated observations, not continuous health
guarantees; workers should check reachability before proofs. Stopped/failed records are omitted.
Independent code reviewers receive no mutable service observations. Refresh the brief after a
replacement service starts; an already running worker keeps its original brief.

The host may run a complete native Falcon simulation through `verification run` because that
runner owns both its mock and assertion counters. No split-process rewrite or in-process fake is
needed. Ordinary VSTest can run through the same host path.

## Retention, recovery and boundaries

Receipts live under the task's `services/SERVICE-ID/`. Immutable `start.json` and `stop.json` are
cited by digest in ordinary evidence events. `service.json` is the latest operational receipt.
The stop receipt includes the retained log digest or a log-retention error. `container.log`
contains the Docker log stream (including Docker's stdout/stderr framing), capped at 5 MiB and
2,000 trailing lines; it is diagnostic output, not complete proof output. Proof artifacts belong
in a verification run's results directory.

The kernel reserves a deterministic container name and an ownership label before contacting
Docker. Startup failure attempts cleanup even when the create response was lost. Interrupted
attempts keep their receipts. Inspect and stop the same ID to reconcile them; a failed cleanup
can be retried. Stop checks the exact ownership label and image before removal and never prunes
containers. Concurrent lifecycle operations on one ID are refused.

The typed launch requests bridge networking, one IPv4 loopback publication, no inherited host
environment or host mounts, a read-only root filesystem, a 64 MiB temporary directory, dropped
capabilities, no new privileges, 512 MiB memory, one CPU, 128 processes, bounded logs, and no
automatic restart. Image-defined defaults still apply. Inspect verifies the running container's
published endpoints before readiness is advertised. These settings use the
[Docker Engine API](https://docs.docker.com/reference/api/engine/version/v1.51/).

This facility does not change provider confinement or guarantee a networking boundary for every
Docker installation. The supported host configuration must preserve loopback publication; verify
loopback reachability and LAN-address refusal when establishing that configuration. The kernel
does not claim remote-machine ingress was tested merely because a same-machine LAN-address probe
was refused.
