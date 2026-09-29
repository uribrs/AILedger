#!/usr/bin/env python3
"""Task-12 offline process/recovery probes. No real model, network service or task mutation.
Usage: python3 tools/EpisodeProbe/run.py /absolute/ailedger NEW_OUTPUT_DIRECTORY
Requires macOS and permission to apply a child sandbox (run outside an enclosing sandbox).
"""
import datetime
import hashlib
import json
import os
import pathlib
import signal
import subprocess
import sys
import time

REPO = pathlib.Path(__file__).resolve().parents[2]
EXE = str(pathlib.Path(sys.argv[1]).resolve())
OUT = pathlib.Path(sys.argv[2]).resolve()
OUT.mkdir(parents=True, exist_ok=False)

def digest(data):
    return hashlib.sha256(data).hexdigest()

def save(path, value):
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n")

subprocess.run([sys.executable, str(REPO / "tools/HandoffProbe/run.py"), EXE, str(OUT / "handoffs")],
               check=True, stdout=(OUT / "handoff.log").open("w"), timeout=180)
request = json.loads((OUT / "handoffs/axonius-request.json").read_text())
request['spec'].update(id='task12-inventory', profile='read-only-audit',
    objective='Inventory this exact package and its explicit omissions; keep semantic sufficiency unknown.',
    non_goals=['No implementation, acceptance, model spend or semantic research.'],
    expected_outputs=['Digest-bound record and omission counts with explicit uncertainty.'],
    acceptance_checks=['Account for record and omission counts at the exact package digest.'])
prepared = subprocess.run([EXE, 'handoff', 'prepare', '--root', request['ledger_identity'],
    '--task', json.loads(json.loads((OUT / 'handoffs/axonius-handoff.json').read_text())['package_json'])['snapshot']['task_id'],
    '--actor', 'operator', '--body-stdin'], input=json.dumps(request), text=True, capture_output=True, timeout=30)
assert prepared.returncode == 0, prepared.stderr
HANDOFF = json.loads(prepared.stdout)
save(OUT / 'audit-handoff.json', HANDOFF)
PACKAGE = json.loads(HANDOFF["package_json"])
SOURCE = pathlib.Path(PACKAGE["ledger_identity"]) / PACKAGE["snapshot"]["task_id"] / "events.jsonl"
SOURCE_HASH = digest(SOURCE.read_bytes())

# Only a deterministic inventory audit. This is not a model research result or assurance verdict.
SCRIPT = r'''#!/usr/bin/ruby --disable-gems
require 'json'
x = JSON.parse(STDIN.read)
h = x.fetch('package'); p = JSON.parse(h.fetch('package_json'))
prior = x.fetch('prior_results')
resumed = prior.any? { |r| r['kind'] == 'submission' }
checkpoint = {'schema_version'=>1,'episode_id'=>p['spec']['id'],'package_sha256'=>h['package_sha256'],
 'status'=>'partial','summary'=>'Read-only package inventory opened; semantic acceptance remains unknown.',
 'recorded_outputs'=>[],'checks'=>p['spec']['acceptance_checks'].map { |c|
 {'check'=>c,'status'=>'not_checked','evidence'=>'Scripted inventory only','limitation'=>'No cognitive evaluation'} },
 'uncertainty'=>['Client judgment and acceptance remain pending.'],'stop_reasons'=>[],
 'additional_reads'=>nil,'host_execution_receipt'=>nil}
def submit(key, result)
 puts JSON.generate({'schema_version'=>1,'operation'=>'submit_result','submission'=>{'schema_version'=>1,'request_id'=>key,'result'=>result}})
 STDOUT.flush
end
submit('checkpoint', checkpoint)
__ACTION__
result = checkpoint.merge('status'=>'reported_complete','checks'=>p['spec']['acceptance_checks'].map { |c| {'check'=>c,'status'=>'observed_pass','evidence'=>"#{p['inputs'].length} records, #{p['inputs'].count { |i| i['record_json'].nil? }} omissions at #{h['package_sha256']}",'limitation'=>'Inventory only; semantic sufficiency unknown'} },'summary'=>"Inventory inspected: #{p['inputs'].length} versioned records, #{p['inputs'].count { |i| i['record_json'].nil? }} explicit omissions. No assertion of semantic sufficiency.")
submit('inventory', result)
'''


def fixture(name, action="", seconds=60):
    folder = OUT / name
    folder.mkdir()
    bridge = folder / "bridge"
    bridge.write_text(SCRIPT.replace("__ACTION__", action))
    bridge.chmod(0o500)
    binding = dict(task_id=dict(value=PACKAGE['snapshot']['task_id']),
                   actor_id=dict(value=PACKAGE['snapshot']['actor_id']), run_id=None,
                   correlation_id="task12-" + name, allow_inspect=True, allow_runless=True)
    authority = dict(schema_version=1, principal="scripted-client", enabled=True,
        expires_at=(datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(hours=1)).isoformat(),
        package_sha256=HANDOFF['package_sha256'], ledger_root=PACKAGE['ledger_identity'], inspection=binding,
        grants=PACKAGE['spec']['requested_grants'], allow_submit_result=True, executable=str(bridge),
        executable_sha256=digest(bridge.read_bytes()), maximum_invocations=4, maximum_attempts=3,
        maximum_seconds=seconds, sources=[])
    start = dict(schema_version=1, request_id=name, handoff=HANDOFF)
    save(folder / "authority.json", authority)
    save(folder / "start.json", start)
    return folder


def command(folder, operation):
    return [EXE, "episode", operation, "--authority", str(folder / "authority.json"),
            "--store", str(folder / "store"), "--scratch", str(folder / "scratch")]


def run(folder, operation="run"):
    p = subprocess.run(command(folder, operation) + ["--body-stdin"], input=(folder / "start.json").read_text(),
                       text=True, capture_output=True, timeout=90)
    if not p.stdout:
        raise AssertionError((p.returncode, p.stderr))
    result = json.loads(p.stdout)
    save(folder / (operation + "-result.json"), result)
    return result


def records(folder):
    rows = []
    for path in sorted((folder / "store/episodes-v1").glob("*/*.json")):
        envelope = json.loads(path.read_text())
        assert digest(envelope['record_json'].encode()) == envelope['sha256']
        rows.append(json.loads(envelope['record_json']))
    return rows


def submissions(result):
    return [r['data'] for r in result['records'] if r['kind'] == 'submission']


def inspect(folder):
    p = subprocess.run(command(folder, 'inspect') + ['--request-id', folder.name], text=True, capture_output=True, timeout=20)
    assert p.returncode == 0, p.stderr
    return json.loads(p.stdout)


def interrupt(folder, crash):
    with (folder / 'host.out').open('w') as stdout, (folder / 'host.err').open('w') as stderr:
        p = subprocess.Popen(command(folder, 'run') + ['--body-stdin'], stdin=subprocess.PIPE,
                             stdout=stdout, stderr=stderr, text=True, start_new_session=True)
        p.stdin.write((folder / 'start.json').read_text()); p.stdin.close()
        deadline = time.monotonic() + 20
        while not any(r['kind'] == 'submission' for r in records(folder)):
            if p.poll() is not None or time.monotonic() >= deadline:
                raise AssertionError('No partial submission before interrupt: ' + (folder / 'host.err').read_text())
            time.sleep(0.05)
        if crash:
            os.killpg(p.pid, signal.SIGKILL)  # Stop the entire disposable provider group, then attest that fact.
        else:
            p.send_signal(signal.SIGTERM)
        p.wait(timeout=15)
    result = inspect(folder)
    save(folder / 'interrupted-result.json', result)
    assert result['execution_outcome'] == ('unknown' if crash else 'cancelled'), result['execution_outcome']
    assert result['authored_status'] == 'partial'
    if crash:
        assert result['reconciliation_required']
        refusal = subprocess.run(command(folder, 'resume') + ['--body-stdin'],
            input=(folder / 'start.json').read_text(), text=True, capture_output=True, timeout=20)
        assert refusal.returncode != 0 and 'reconcile' in refusal.stderr
        reconciled = subprocess.run(command(folder, 'reconcile') + ['--request-id', folder.name, '--confirm-provider-stopped'],
                                   text=True, capture_output=True, timeout=20)
        assert reconciled.returncode == 0, reconciled.stderr
        save(folder / 'reconciled-result.json', json.loads(reconciled.stdout))
    resumed = run(folder, 'resume')
    assert resumed['execution_outcome'] == 'succeeded', resumed['execution_outcome']
    assert len(submissions(resumed)) == 2
    assert submissions(result)[0]['receipt'] == submissions(resumed)[0]['receipt']
    return resumed


results = []
plain = fixture('inventory')
result = run(plain)
assert result['execution_outcome'] == 'succeeded', [(r['kind'], r['data']) for r in result['records'] if r['kind'] in ('stderr','process','blocked')]
assert result['acceptance'] == 'not_assessed' and len(submissions(result)) == 2
assert any(r['kind'] == 'usage_unavailable' and r['data']['cost_usd'] is None for r in result['records'])
replay = run(plain)
assert result['records'] == replay['records']
results.append(dict(case='inventory-and-lost-response-replay', outcome='passed', execution_id=result['execution_id']))

for name, crash in [('cancel', False), ('host-kill', True)]:
    folder = fixture(name, "sleep 30 unless resumed")
    result = interrupt(folder, crash)
    results.append(dict(case=name + '-and-resume', outcome='passed', execution_id=result['execution_id']))

folder = fixture('timeout', 'sleep 10', seconds=2)
result = run(folder)
assert result['execution_outcome'] == 'blocked' and result['authored_status'] == 'partial'
assert run(folder, 'resume')['execution_outcome'] == 'blocked'
results.append(dict(case='elapsed-budget', outcome='passed', execution_id=result['execution_id']))

folder = fixture('failed', 'exit 7')
result = run(folder)
assert result['process_outcome'] == 'failed' and result['authored_status'] == 'partial'
results.append(dict(case='provider-failure', outcome='passed', execution_id=result['execution_id']))

folder = fixture('retrieval', r'''unless prior.any? { |r| r['kind'] == 'tool' && r['data']['operation'] == 'retrieve_context' }
 q = p['inputs'].find { |i| i['record_json'].nil? }['reference']['retrieve']
 puts JSON.generate({'schema_version'=>1,'operation'=>'retrieve_context','retrieval'=>q})
 STDOUT.flush
 exit 0
end''')
result = run(folder)
assert result['execution_outcome'] == 'succeeded'
assert len([r for r in result['records'] if r['kind'] == 'invocation']) == 2
results.append(dict(case='authorized-retrieval-follow-up', outcome='passed', execution_id=result['execution_id']))

# The bridge deliberately tries to read its host grant and write outside its directory.
# It has neither path permission; these are fixture-only boundary probes.
folder = OUT / 'sandbox'
action = """require 'socket'
[proc { File.read(%s) }, proc { File.write(%s, 'forbidden') }, proc { TCPSocket.new('127.0.0.1', 9) }].each do |attempt|
 begin
  attempt.call
  abort 'sandbox unexpectedly permitted a forbidden action'
 rescue Errno::EPERM, Errno::EACCES
  STDERR.puts 'Expected sandbox denial'
 end
end
""" % (json.dumps(str(folder / 'authority.json')), json.dumps(str(OUT / 'forbidden-write')))
folder = fixture('sandbox', action)
result = run(folder)
assert result['execution_outcome'] == 'succeeded', [(r['kind'],r['data']) for r in result['records'] if r['kind'] in ('stderr','process')]
assert not (OUT / 'forbidden-write').exists()
assert len([r for r in result['records'] if r['kind'] == 'stderr' and 'Expected sandbox denial' in r['data']['text']]) == 3
results.append(dict(case='sandbox-read-write-network-denials', outcome='passed', execution_id=result['execution_id']))

assert digest(SOURCE.read_bytes()) == SOURCE_HASH
report = dict(identity=subprocess.check_output([EXE, 'version'], text=True).strip(),
              cases=results, source_sha256=SOURCE_HASH, client_acceptance='pending',
              real_provider_episodes=0, usage='unavailable stays null; scripts are not model usage',
              limitation='Mechanical offline execution and recovery only; no judgment, cost or delivery improvement claimed.')
save(OUT / 'report.json', report)
print(json.dumps(report, indent=2))
