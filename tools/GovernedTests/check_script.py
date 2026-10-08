"""Exercise the shell wrapper's boundaries without rebuilding the solution."""

import json
import os
from pathlib import Path
import subprocess
import shutil
import tempfile


FAKE_DOTNET = """#!/usr/bin/env python3
import json, os, pathlib, sys
args = sys.argv[1:]
with open(os.environ['PROBE_CALLS'], 'a') as stream:
    stream.write(json.dumps({'args': args, 'cwd': os.getcwd()}) + '\\n')
if args[0] == 'build':
    if os.environ.get('PROBE_BUILD_FAIL') == '1':
        print('deliberate build failure')
        sys.exit(19)
    output = pathlib.Path(args[args.index('--artifacts-path') + 1]) / 'bin'
    for name in ['GovernedTests', 'AILedger.Tests', 'AILedger.Memory.Tests']:
        directory = output / name / 'debug'
        directory.mkdir(parents=True, exist_ok=True)
        for suffix in ['.dll', '.deps.json', '.runtimeconfig.json']:
            (directory / (name + suffix)).touch()
else:
    if os.environ.get('PROBE_TEST_FAIL') == '1':
        print('deliberate suite failure')
        sys.exit(23)
    suite = 'AILedger.Memory.Tests' if 'Memory.Tests' in args[2] else 'AILedger.Tests'
    print(suite + ': total=1, failed=0, skipped=0, errors=0')
"""


def main():
    repo = Path(__file__).resolve().parents[2]
    script = repo / 'scripts/test-governed.sh'
    with tempfile.TemporaryDirectory(prefix="governed script's checks-") as temporary:
        root = Path(temporary)
        fake = root / 'dotnet'
        fake.write_text(FAKE_DOTNET)
        fake.chmod(0o755)
        log = root / 'calls.jsonl'
        environment = dict(os.environ, PATH=str(root) + os.pathsep + os.environ['PATH'],
                           TMPDIR=str(root), PROBE_CALLS=str(log))

        def run(arguments, expected, **overrides):
            log.unlink(missing_ok=True)
            result = subprocess.run(['sh', str(script), *arguments], cwd=root,
                                    env=dict(environment, **overrides),
                                    capture_output=True, text=True, timeout=30)
            assert result.returncode == expected, (result.returncode, result.stdout, result.stderr)
            return [json.loads(line) for line in log.read_text().splitlines()] if log.exists() else []

        calls = run(['AILedger.Tests', 'Exact Filter'], 0)
        assert all(Path(call['cwd']) == repo for call in calls)
        assert calls[-1]['args'][-1] == 'Exact Filter'
        assert len(calls) == 3

        calls = run([], 0)
        runs = [call for call in calls if call['args'][0] == 'exec']
        assert len(runs) == 2 and 'AILedger.Memory.Tests' in runs[0]['args'][2]

        calls = run([], 1, PROBE_BUILD_FAIL='1')
        assert len(calls) == 1 and calls[0]['args'][0] == 'build'

        calls = run([], 1, PROBE_TEST_FAIL='1')
        runs = [call for call in calls if call['args'][0] == 'exec']
        assert len(runs) == 1 and 'AILedger.Memory.Tests' in runs[0]['args'][2]

        durable = root / 'retained output'
        scratch = root / 'disposable scratch'
        scratch.mkdir()
        for overrides in ({}, {'PROBE_BUILD_FAIL': '1'}, {'PROBE_TEST_FAIL': '1'}):
            previous = set(durable.glob('tests.*'))
            run(['AILedger.Tests'], 1 if overrides else 0,
                AILEDGER_RUN_OUTPUT=str(durable), TMPDIR=str(scratch), **overrides)
            retained = (set(durable.glob('tests.*')) - previous).pop()
            shutil.rmtree(scratch)
            scratch.mkdir()
            assert (retained / 'solution-build.log').exists()
            if 'PROBE_BUILD_FAIL' not in overrides:
                text = (retained / 'AILedger.Tests.log').read_text()
                assert ('deliberate suite failure' if overrides else 'total=1') in text
            else:
                assert 'deliberate build failure' in (retained / 'solution-build.log').read_text()

        before = set(repo.iterdir())
        assert run([], 2, TMPDIR=str(repo)) == []
        assert set(repo.iterdir()) == before
        assert run(['invalid-suite'], 2) == []
        assert run(['all', 'filter', 'extra'], 2) == []
    print('PASS: cwd, paths with spaces/apostrophe, filter, both suites, build failure, '
          'test failure, retained success/failure logs after scratch deletion, source-tree TMPDIR rejection and invalid arguments.')


if __name__ == '__main__':
    main()
