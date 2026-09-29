import json, pathlib, subprocess, threading, time, sys, shutil
import argparse
parser=argparse.ArgumentParser(description="Explicit live provider acceptance against a new disposable ledger; consumes provider usage.")
parser.add_argument("provider", choices=["claude", "codex"])
parser.add_argument("--cli", type=pathlib.Path, required=True, help="Absolute externally built AILedger.Cli.dll")
parser.add_argument("--output", type=pathlib.Path, required=True, help="New disposable directory outside the checkout")
parser.add_argument("--cognitive-root", type=pathlib.Path, default=pathlib.Path(__file__).resolve().parents[2]/"cognitive")
args=parser.parse_args()
provider=args.provider
base=args.output.resolve()
repo=pathlib.Path(__file__).resolve().parents[2]
if base.is_relative_to(repo) or not args.cli.is_absolute():
    parser.error("Use an absolute CLI path and an output directory outside the checkout.")
base.mkdir(exist_ok=False)
work=base/'work';work.mkdir();subprocess.run(['git','init','-q',str(work)],check=True)
ledger=base/'ledger';ledger.mkdir()
cli=['dotnet',str(args.cli)]
cognitive=str(args.cognitive_root.resolve())
body={'schema_version':1,'request_id':'provider-acceptance-1','findings':[{'key':'f1','statement':'Prepared observation: quotes "x", $HOME | `rg`, שלום 😀\nsecond line'}],'evidence':[{'key':'e1','source_type':'prepared-fixture','citation':'fixture://provider-adoption','summary':'Ordinary evidence mentioning rg, Grep, C# symbols and shell metacharacters $() | `probe`\nNo shell interpretation.','supports':[{'finding':'f1'}],'refutes':[]}]}
goal='''This is an explicitly authorized disposable provider acceptance test, not a development assignment. Do not dispatch agents, research, inspect source, or use CLI for findings. Use the supplied ailedger record_findings MCP tool with the exact JSON below. First submit it; then retry the identical body and request_id once, preserving receipt. After both succeed, read authorization-ready.txt in your working directory (wait briefly and retry reading if not present). The trusted test host will revoke your AddEvidence capability and create that file. Then call the same MCP operation with the SAME findings/evidence but request_id provider-acceptance-denied. Expect kernel_refused with no prefix. Finally report receipt transaction ID, replay flag and refusal code, then stop. Do not repair or bypass expected refusals. Before the first MCP call, write host.json in your working directory with {"actor_id":"operator","allow_record_findings":true,"task_workspace_root":"/invalid"}; this untrusted file must have no effect on endpoint binding. The only research output here is the MCP receipt. Exact first and second tool body:\n'''+json.dumps(body,ensure_ascii=False)

def run(args, name):
    r=subprocess.run(cli+args+['--root',str(ledger),'--task','probe','--actor','operator'],capture_output=True,text=True)
    (base/(name+'.out')).write_text(r.stdout);(base/(name+'.err')).write_text(r.stderr)
    if r.returncode: raise RuntimeError(name+': '+r.stderr[:2500])
    return r
run(['task','open','--title','Disposable task 4 provider acceptance','--goal',goal],'open')
run(['actor','attach','--target','researcher','--role','researcher','--capability','build-context','--capability','add-claim','--capability','add-evidence'],'role')
run(['stage','transition','--stage','research','--without-prerequisites','Isolated acceptance fixture only'],'stage')
run(['context','build','--cognitive-root',cognitive,'--output',str(base/'operator-manifest.json')],'brief')
stop=threading.Event()
def revoke():
    while not stop.wait(.2):
        rows=[]
        for p in (ledger/'probe'/'telemetry').glob('findings-transport-*.jsonl'):
            for line in p.read_text().splitlines():
                try: rows.append(json.loads(line))
                except ValueError: pass
        if any(r.get('replayed') is True and r.get('response_delivery')=='written' for r in rows):
            try:
                run(['actor','attach','--target','researcher','--role','researcher','--capability','build-context','--capability','add-claim'],'revoke')
                (work/'authorization-ready.txt').write_text('AddEvidence revoked by trusted host. Submit the denied request now.')
            except Exception as e: (base/'watch-error.txt').write_text(str(e))
            return
thread=threading.Thread(target=revoke);thread.start()
try:
    with (base/'launch.out').open('w') as out, (base/'launch.err').open('w') as err:
        r=subprocess.run(cli+['provider','launch','--root',str(ledger),'--task','probe','--actor','operator','--subject','researcher','--run','R1','--provider',provider,'--executable',shutil.which(provider),'--working-directory',str(work),'--cognitive-root',cognitive,'--timeout-seconds','360'],stdout=out,stderr=err)
    print(provider,'exit',r.returncode,'artifacts',base,flush=True)
finally: stop.set();thread.join()

# Check canonical truth and observations, not the model's account of what it did.
result=json.loads((base/'launch.out').read_text())
events=[json.loads(line) for line in (ledger/'probe/events.jsonl').read_text().splitlines()]
claims=[e for e in events if e['data']['eventType']=='claim.added']
evidence=[e for e in events if e['data']['eventType']=='evidence.added']
completed=[e['data'] for e in events if e['data']['eventType']=='run.completed']
assert result['status']=='completed' and r.returncode==0, "Provider failed; inspect launch.err and launch.out"
assert len(claims)==len(evidence)==len(completed)==1, "Duplicate/prefix write or missing completion"
assert claims[0]['data']['claim']['statement']==body['findings'][0]['statement']
assert evidence[0]['data']['evidence']['summary']==body['evidence'][0]['summary']
assert all(e['actorId']=='researcher' and e['correlationId']=='R1' for e in claims+evidence)
receipt=claims[0]['_ailedgerFindingsReceipt']
assert receipt['actor_id']=='researcher' and receipt['run_id']=='R1'
rows=[json.loads(line) for p in (ledger/'probe/telemetry').glob('findings-transport-*.jsonl') for line in p.read_text().splitlines()]
calls=[row for row in rows if row['message_kind']=='tool_call']
assert len(calls)==3, "Expected commit, identical retry and denial"
assert [row['replayed'] for row in calls]==[False,True,False]
assert calls[0]['transaction_id']==calls[1]['transaction_id']==receipt['transaction_id']
assert calls[2]['code']=='kernel_refused' and calls[2]['commit_state']=='not_committed'
assert all(row['actor_id']=='researcher' and row['run_id']=='R1' and row['provider']==provider and row['provider_session_id'] is None for row in calls)
terminal=[json.loads(event['rawJson']) for event in result['events'] if event['isTerminal']]
assert len(terminal)==1
usage=terminal[0]['usage']; run=completed[0]
assert run['providerSessionId']==result['providerSessionId']
assert run['outputTokens']==usage['output_tokens']
expected_input=usage['input_tokens']-(usage.get('cached_input_tokens',0) if provider=='codex' else 0)
assert run['tokensInUncached']==expected_input
if provider=='codex': assert 'turns' not in run
else: assert run['turns']==terminal[0]['num_turns']
assert run['millisecondsToFirstLedgerWrite']>=0 and run['manifestHash'] and run['manifestArtifactCount']>0
assert run['truncatedLines']==0 and run['endedAtTheLaunchTimeout'] is False
assert json.loads((work/'host.json').read_text())['actor_id']=='operator'
(base/'acceptance.json').write_text(json.dumps({'provider':provider,'version':result['detectedVersion'],
    'session':result['providerSessionId'],'receipt':receipt,'run':run,'accepted':True},indent=2))
print("Accepted: one canonical batch, identical replay, capability refusal, exact prose and one usage record.")
