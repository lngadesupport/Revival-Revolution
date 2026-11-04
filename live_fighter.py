"""Loopback packet bridge for a main-thread, real-fighter Gekko session.

The internet-facing Session authenticates every packet before calling receive.
The game socket is loopback-only and uses a separate random local capability.
"""
import base64,hmac,json,pathlib,secrets,socket,time

class LiveFighter:
    def __init__(self,root,slot,bridge,selection,atomic):
        self.root=pathlib.Path(root);self.slot=slot;self.bridge=bridge;self.selection=selection;self.atomic=atomic
        self.sock=socket.socket(socket.AF_INET,socket.SOCK_DGRAM);self.sock.bind(('127.0.0.1',0));self.sock.setblocking(False)
        self.key=secrets.token_bytes(16);self.endpoint=None;self.started=False;self.committed=False;self.loaded=None;self.action=None;self.test_id='';self.ready=False;self.state='idle';self.next_status=0;self.report=None;self.scripted=False
        self.finished=False;self.completion=None;self.result_candidate=None;self.result_since=0
        self.report_stamp=None;self.last_report_seen=None
    def wire(self):return dict(ready=self.ready,test_id=self.test_id,loaded=self.loaded,committed=self.committed,scripted=self.scripted,completion=self.completion)
    def read_report(self,name):
        r=json.loads((self.root/name).read_text(encoding='utf-8-sig'))
        if not isinstance(r,dict) or r.get('session_id')!=self.bridge.session or r.get('test_id')!=self.test_id:raise RuntimeError('Live fighter report belongs to another session')
        return r
    def refresh_report(self):
        path=self.root/'live-fighter.json'
        try:
            stat=path.stat()
            stamp=(stat.st_mtime_ns,stat.st_size)
            if stamp!=self.report_stamp:
                report=self.read_report('live-fighter.json')
                self.report=report;self.report_stamp=stamp;self.last_report_seen=time.monotonic()
        except (OSError,ValueError):
            # The bridge may replace the report while it is being read. Keep the
            # last complete report, but never extend its heartbeat on a failed read.
            pass
        if self.last_report_seen is None or time.monotonic()-self.last_report_seen>5:
            raise RuntimeError('Game live fighter heartbeat expired')
    def step(self,other):
        other=other or {}
        if not isinstance(other,dict):raise RuntimeError('Invalid live fighter setup')
        if not self.ready:
            try:r=json.loads((self.root/'fighter-test-request.json').read_text())
            except (OSError,ValueError):r={}
            if r.get('session_id')==self.bridge.session and r.get('mode') in ('live_keyboard','live_scripted'):
                self.scripted=r['mode']=='live_scripted';self.ready=True;self.state='waiting_for_peer';(self.root/'fighter-test-active.txt').write_text(self.bridge.session)
        if self.ready and other.get('ready') is True:
            if other.get('scripted',False)!=self.scripted:raise RuntimeError('Both PCs must choose the same live input mode')
            if not self.test_id:
                if self.slot==0:self.test_id=secrets.token_hex(12)
                else:
                    proposed=other.get('test_id')
                    if isinstance(proposed,str) and len(proposed)==24 and all(c in '0123456789abcdef' for c in proposed):self.test_id=proposed
            if self.test_id and self.loaded is None and self.action is None:
                self.bridge.begin('fighter_test_load',test_id=self.test_id,setup_hash=self.selection.proposal_hash);self.action='load';self.state='loading'
        if self.action:
            reply=self.bridge.poll()
            if reply:
                if self.action=='load':
                    if reply!='fighter_loaded':raise RuntimeError('Live fighter load failed')
                    self.loaded=self.read_report('fighter-load.json');self.state='loaded'
                else:
                    if reply!='live_started':raise RuntimeError('Live fighter start failed')
                    report=self.read_report('live-fighter.json');self.report=report;self.report_stamp=None;self.last_report_seen=None
                    self.refresh_report();report=self.report;port=report.get('port')
                    if type(port) is not int or not 1<=port<=65535:raise RuntimeError('Invalid game loopback endpoint')
                    self.endpoint=('127.0.0.1',port);self.started=True;self.state='synchronizing'
                self.action=None
        if self.loaded and other.get('test_id')==self.test_id and other.get('loaded'):
            remote=other['loaded']
            if not isinstance(remote,dict) or remote.get('slot')!=1-self.slot or remote.get('session_id')!=self.bridge.session or remote.get('test_id')!=self.test_id:raise RuntimeError('Invalid remote live fighter ownership')
            for name in ('setup_hash','frame','checksum','actions'):
                if remote.get(name)!=self.loaded.get(name):raise RuntimeError('Live fighter baselines differ: '+name)
            if self.loaded.get('frame')!=0 or self.loaded.get('setup_hash')!=self.selection.proposal_hash:raise RuntimeError('Invalid live fighter baseline')
            self.committed=True
            if other.get('committed') is True and not self.started and not self.action:
                config=json.dumps(dict(port=self.sock.getsockname()[1],key=self.key.hex(),scripted=self.scripted))
                self.bridge.begin('live_start',test_id=self.test_id,fighter_json=config);self.action='start';self.state='starting'
        if self.started:
            # Managed status writes can race with readers. Retain the last complete
            # report, but expire it if no new complete heartbeat arrives for 5 seconds.
            self.refresh_report()
            report=self.report
            self.state=report.get('state','synchronizing')
            if report.get('state')=='error':raise RuntimeError(report.get('error','Live fighter failure'))
            match=report.get('match') or {}
            if match.get('state')==6:
                candidate=dict(test_id=self.test_id,frame=report.get('frame'),round=match.get('round'),winner=match.get('winner'),checksum=match.get('checksum'))
                if candidate!=self.result_candidate:self.result_candidate=candidate;self.result_since=time.monotonic()
                if time.monotonic()-self.result_since>=2 and isinstance(candidate['checksum'],str):self.completion=candidate
                remote_completion=other.get('completion')
                if self.completion and remote_completion:
                    if self.completion!=remote_completion:raise RuntimeError('Peer final match result/checksum differs')
                    self.finished=True;self.state='completed'
            else:self.result_candidate=None;self.completion=None
        if time.monotonic()>=self.next_status:
            self.next_status=time.monotonic()+.2
            text='Live keyboard/controller play: '+self.state+'\nPlayer '+str(self.slot+1)+' | W/S/D/A, J/K/U/I/O. Hide panel with F1.\n'
            if self.started:text+='Frame: '+str(report.get('frame',0))+' | rollback advances: '+str(report.get('rollback_advances',0))+' | desyncs: '+str(report.get('desyncs',0))+'\n'
            if self.started:text+='Input: '+str(report.get('input_device','Keyboard'))+'\nPad: D-pad/stick; west/north attacks; south jump; east dash; right shoulder ultimate. F2 cycles pads.\n'
            text+='Experimental live rollback. Disconnect stops the peer session.'
            (self.root/'live-fighter-summary.txt').write_text(text,encoding='utf-8')
    def packets(self):
        if not self.endpoint:return
        for _ in range(256):
            try:data,endpoint=self.sock.recvfrom(60017)
            except (BlockingIOError,ConnectionResetError):return
            if endpoint==self.endpoint and 16<len(data)<=60016 and hmac.compare_digest(data[:16],self.key):
                yield dict(test_id=self.test_id,data=base64.b64encode(data[16:]).decode())
    def receive(self,payload):
        if not self.endpoint:return
        if not isinstance(payload,dict) or payload.get('test_id')!=self.test_id:return
        value=payload.get('data')
        if not isinstance(value,str) or len(value)>80000:raise RuntimeError('Invalid live fighter packet')
        try:data=base64.b64decode(value,validate=True)
        except ValueError:raise RuntimeError('Invalid live fighter packet encoding')
        if not 0<len(data)<=60000:raise RuntimeError('Invalid live fighter packet size')
        try:self.sock.sendto(self.key+data,self.endpoint)
        except (BlockingIOError,ConnectionResetError):pass
    def close(self):
        self.sock.close()
        if self.started:
            report=self.report or {};self.state='completed' if self.finished else 'error' if report.get('state')=='error' else 'stopped'
            footer='Match completed with matching final checksums. Local controls restored.' if self.finished else str(report.get('error') or 'Peer session ended. See live-desync.json for the retained trace.')
            (self.root/'live-fighter-summary.txt').write_text('Live play: '+self.state+'\nFrame: '+str(report.get('frame',0))+' | rollback advances: '+str(report.get('rollback_advances',0))+' | desyncs: '+str(report.get('desyncs',0))+'\n'+footer,encoding='utf-8')

