# Tool and environment inventory

Consult this inventory before searching for, replacing, or installing a capability a task needs.
It records working access paths, including skills and MCP tools, rather than merely installed names.
The initial entries describe the current macOS development host; they are not portable defaults.

## How to use and maintain this list

1. Find the capability below and check its execution context: coordinator, child session, host,
   repository environment, or container. Pass the relevant entry to a worker that needs it.
2. Use its documented setup and a small availability check. An installed tool may still be absent
   from this session's PATH, MCP configuration, grants, or sandbox.
3. If the capability is missing from this list, investigate it: inspect the current session's exposed
   tools and skills, repository setup instructions, existing environments, and known installations.
   This inventory is a starting point, not an exhaustive allow-list or a reason to stop.
4. If a listed route fails, distinguish missing software, missing configuration, and denied access.
   Follow the permitted recovery path. An access denial does not authorize bypassing the boundary.
   Install or reconfigure only within the task's authorization; do not reinstall merely because a
   sandbox cannot reach an existing tool.
5. Once a working route is confirmed, add or correct its entry **here**, with a check date and evidence.
   Record unresolved failures as such. Never store credentials here. Keep tool names, paths, setup,
   and checks in this file; the manual and operator guide retain their generic links unchanged.

These are operating instructions, not additional executable kernel guards. Task-specific findings
and decisions still belong in the task's normal evidence record.

## Confirmed capabilities

### Java and Spark

- **Location:** host Homebrew OpenJDK, not Docker:
  `/opt/homebrew/opt/openjdk@17/libexec/openjdk.jdk/Contents/Home`.
- **Setup:** set `JAVA_HOME` to that directory and prepend `$JAVA_HOME/bin` to `PATH` in the test
  environment. Use the parser Python environment below.
- **Check:** invoke that directory's `bin/java -version`. OpenJDK **17.0.20** was verified on
  **2026-09-24**. Finding `/usr/bin/java` alone does not establish a usable Java runtime.
- **Boundary:** Spark also needs local Py4J sockets. A child sandbox can deny those even after Java
  is found. Use an authorized host-side `ailedger verification run` with the repository's profile.
- **Evidence:** task `2026-09-23_1034-axonius-yaml-bundle`, K39/E16/E20; task
  `2026-09-23_2014-axonius-parser-fixes`, `verification/E3/result.json` records a passed host run.

### Python, pytest and PySpark

- **Location:** `/Users/user/Dev/cymulate-integration-parsers/.venv/bin/python`.
- **Setup:** run from the intended parser checkout with `PYTHONPATH=libs/packages`; this lets the
  existing interpreter test that checkout. Use `PYTHONDONTWRITEBYTECODE=1` when avoiding bytecode
  writes. Spark additionally needs the Java setup above.
- **Check:** this interpreter imported pytest and PySpark on **2026-09-24**: Python **3.10.21**,
  pytest **9.0.2**, PySpark **3.3.0**. Imports establish availability, not a passing Spark suite.
- **Boundary:** `uv` was not on the inspected PATH; the existing virtualenv was usable without it.
  Do not assume the environment's dependencies match every other repository.
- **Evidence:** Axonius bundle K39 and parser follow-up E3, plus the dated interpreter/import check.

### Docker through Colima

- **Location:** `/opt/homebrew/bin/docker`, `/opt/homebrew/bin/colima`; Docker socket
  `/Users/user/.colima/default/docker.sock`.
- **Setup:** prefer the repository's Docker verification profile through the authorized coordinator.
  Let that profile and the kernel supply their environment. For a permitted direct host probe, the
  endpoint is `unix:///Users/user/.colima/default/docker.sock` (`docker --host ... info`).
- **Boundary:** socket existence is not connectivity. Inside a sandbox, even `colima status` reported
  "not running" while the coordinator could reach the daemon. Do not start or reinstall it solely
  on that observation. Testcontainers socket overrides and cleanup settings are profile-specific.
- **Evidence:** successful host connectivity and container tests on **2026-09-23**: Axonius bundle
  E10 (1,749 passed, one skipped); container-verification task OE5/PV4. Socket presence rechecked
  **2026-09-24**; daemon health was not re-probed then.

### .NET tests in governed sessions

- **Location:** `scripts/test-governed.sh` in the AILedger checkout.
- **Setup:** for this repository, invoke `sh scripts/test-governed.sh` from its root; it also accepts
  a test-project name and a test-name filter. This is the shared socket-free xUnit runner.
- **Boundary:** ordinary VSTest can fail on sandbox IPC sockets. The shared runner does not grant
  Docker, Spark, network, or arbitrary filesystem access. Tests needing those may require an
  authorized host verification profile; record setup errors separately from assertion failures.
- **Evidence:** container-verification TW4 reused the runner on **2026-09-23**. Manual reviewer Q2
  reached a separate application-data write denial; that partial run is not a full-suite pass.

### NuGet restore and cached dependencies

- **Location:** the applicable developer/repository `NuGet.config` and user cache `~/.nuget/packages`.
- **Setup:** when a child's feeds are inaccessible and required packages are absent, have an
  authorized host process restore the intended solution with its correct configuration. The child
  can then build with `--no-restore` where its own restored assets are current.
- **Boundary:** a populated global cache alone does not create `project.assets.json` in a different
  checkout. Check that checkout's restore state. Do not copy credentials into briefs or this file.
- **Evidence:** Axonius bundle BE2 → coordinator E8, **2026-09-23**: host restore supplied the missing
  pinned packages; subsequent verification built successfully.

### Roslyn MCP for C# navigation

- **Access:** inspect the current session's exposed MCP tools. Governed Claude and Codex children
  have used Roslyn successfully; the coordinating session in the manual task had no Roslyn exposed.
  A child having it does not imply its parent has it, or vice versa.
- **Setup/check:** select and load the intended repository's solution through the available bridge,
  then perform a targeted symbol query. Verify the selected solution and authorized repository scope.
- **Boundary:** installation alone does not expose MCP tools in an existing session. Follow the
  existing Roslyn search guard and its recorded-failure fallback; this entry does not waive it.
- **Evidence:** **2026-09-23**, container task worker runs and manual reviewer Q2 used Roslyn;
  manual task OE2 records the coordinator's tool absence.

## Adding a tool, skill or MCP

Add a capability entry above using the same fields: **Location/access**, **Setup/check**,
**Boundary**, and **Evidence with date**. For a skill, identify its exact installed name/path and
when to invoke it. For an MCP, identify the provider/session that exposes it, required connection or
grant, and a successful harmless call. Mark unverified availability explicitly until checked.
Do not duplicate the entry in the manual, operator guide, or individual skills.

Return to the [coordinator operating manual](coordinator-operating-manual.md#reading-and-tooling)
for workflow and tool-use rules.
