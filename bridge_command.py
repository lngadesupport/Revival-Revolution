"""Send a bounded main-thread command to the local game bridge."""
import argparse,json,pathlib,time,uuid,os
root=pathlib.Path(__file__).resolve().parent
parser=argparse.ArgumentParser();parser.add_argument('action',choices=['open_local_vs','open_training','rollback_self_test','snapshot_roundtrip_vs','inspect']);parser.add_argument('--slot',type=int,choices=[0,1],default=0);parser.add_argument('--frames',type=int,choices=range(1,31),default=8);parser.add_argument('--root',type=pathlib.Path,default=root);args=parser.parse_args();root=args.root
pid=int((root/'netplay-pid.txt').read_text().strip());identifier=uuid.uuid4().hex
request=dict(id=identifier,action=args.action,session_id='local-test',target_pid=pid,protocol=1,slot=args.slot,frames=args.frames)
tmp=root/(identifier+'.tmp');tmp.write_text(json.dumps(request));os.replace(tmp,root/'game-command.json')
for _ in range(400):
 try:response=json.loads((root/'game-response.json').read_text())
 except (FileNotFoundError,json.JSONDecodeError):time.sleep(.05);continue
 if response.get('id')==identifier and response.get('state')!='opening_vs':
  print(json.dumps(response,indent=2));raise SystemExit(1 if response.get('state') in ['error','rejected','rollback_test_failed'] else 0)
 time.sleep(.05)
raise SystemExit('Timed out waiting for the local game bridge')
