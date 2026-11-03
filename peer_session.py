"""Direct UDP connection-code preflight and coordinated local VS entry.

This release coordinates owned-slot setup and stops at stage selection. It never
feeds synthetic inputs to the game. MR2 uses AutoPunch; MR1 uses direct networking.
"""
import argparse,base64,ctypes,hashlib,hmac,ipaddress,json,os,pathlib,secrets,socket,struct,time,uuid,zlib
try:
    import msvcrt
except ImportError:
    msvcrt = None
from peer_native import SyntheticPeer
from autopunch_transport import AutoPunchSocket,discover_public_endpoint,ipv4_endpoint,settings
from selection_sync import SelectionSync
try:
    from fighter_input_test import FighterInputTest
except ImportError:
    # The legacy diagnostic is optional; live_fighter is the supported play path.
    FighterInputTest = None
from live_fighter import LiveFighter
PROTOCOL=1
BUILD='metal-99.4.3.895-gekko-9da2363-peer9'

def encode_code(address,port,key,relay=None):
    try:
        host_ip=ipaddress.IPv4Address(address)
    except ipaddress.AddressValueError as exc:
        raise ValueError('Invalid IPv4 host address') from exc
    if type(port) is not int or not 1<=port<=65535:
        raise ValueError('Invalid host UDP port')
    if not isinstance(key,bytes) or len(key)!=16:
        raise ValueError('Connection key must contain exactly 16 bytes')
    if host_ip.is_unspecified or host_ip.is_multicast or int(host_ip)==0xffffffff:
        raise ValueError('Invalid host endpoint address')
    body=host_ip.packed+struct.pack('!H',port)+key
    if relay is not None:
        if not isinstance(relay,(tuple,list)) or len(relay)!=2:
            raise ValueError('Invalid AutoPunch relay endpoint')
        try:
            relay_ip=ipaddress.IPv4Address(relay[0])
        except ipaddress.AddressValueError as exc:
            raise ValueError('Invalid AutoPunch relay address') from exc
        relay_port=relay[1]
        if type(relay_port) is not int or not 1<=relay_port<=65535:
            raise ValueError('Invalid AutoPunch relay port')
        if relay_ip.is_unspecified or relay_ip.is_multicast or int(relay_ip)==0xffffffff:
            raise ValueError('Invalid AutoPunch relay address')
        body+=relay_ip.packed+struct.pack('!H',relay_port)
    prefix='MR2-' if relay is not None else 'MR1-'
    return prefix+base64.b32encode(body+struct.pack('!I',zlib.crc32(body))).decode().rstrip('=')

def decode_code(code):
    endpoint,key,relay=decode_connection_code(code);return endpoint,key

def decode_connection_code(code):
    if not isinstance(code,str):
        raise ValueError('Connection code must be text')
    code=code.strip().upper()
    if code[:4] not in ('MR1-','MR2-') or len(code)>96:raise ValueError('Invalid MR1/MR2 connection code')
    text=code[4:];raw=base64.b32decode(text+'='*((-len(text))%8))
    if len(raw)!=(32 if code.startswith('MR2-') else 26) or zlib.crc32(raw[:-4])!=struct.unpack('!I',raw[-4:])[0]:raise ValueError('Connection code checksum failed')
    address=socket.inet_ntoa(raw[:4]);port=struct.unpack('!H',raw[4:6])[0]
    ip=ipaddress.IPv4Address(address)
    if not port or ip.is_multicast or ip.is_unspecified or int(ip)==0xffffffff:raise ValueError('Invalid code endpoint')
    relay=None
    if code.startswith('MR2-'):
        relay=(socket.inet_ntoa(raw[22:26]),struct.unpack('!H',raw[26:28])[0]);ip=ipaddress.IPv4Address(relay[0])
        if not relay[1] or ip.is_multicast or ip.is_unspecified or int(ip)==0xffffffff:raise ValueError('Invalid AutoPunch relay endpoint')
    return (address,port),raw[6:22],relay

def atomic_json(path,value):
    tmp=path.with_name(path.name+'.'+str(os.getpid())+'.tmp')
    tmp.write_text(json.dumps(value,indent=2),encoding='utf-8')
    try:os.replace(tmp,path)
    except PermissionError:
        try:tmp.unlink()
        except OSError:pass

class GameBridge:
    def __init__(self,root,slot,session):self.root=root;self.slot=slot;self.session=session;self.command=None
    def begin(self,action,**extra):
        pid=int((self.root/'netplay-pid.txt').read_text().strip())
        self.command=dict(id=uuid.uuid4().hex,action=action,session_id=self.session,target_pid=pid,protocol=1,slot=self.slot,frames=8)
        self.command.update(extra)
        atomic_json(self.root/'game-command.json',self.command);self.deadline=time.monotonic()+(120 if action=='live_local_test' else 65 if action=='fighter_test_load' else 18)
    def poll(self):
        if time.monotonic()>self.deadline:raise RuntimeError('Game bridge timed out; return to the main menu and attach the launcher')
        try:r=json.loads((self.root/'game-response.json').read_text())
        except (OSError,ValueError):return None
        if r.get('id')!=self.command['id'] or r.get('pid')!=self.command['target_pid'] or r.get('state') in ('opening_vs','selection_applying','selection_waiting','fighter_loading','fighter_capturing','live_trial_running'):return None
        if r.get('state') not in ('peer_prepared','vs_ready','selection_ready','selection_previewed','selection_applied','selection_ended','fighter_loaded','fighter_captured','fighter_replayed','fighter_replay_failed','live_started','live_trial_passed','live_trial_failed'):raise RuntimeError(r.get('message','Game bridge rejected the session'))
        return r['state']

class Session:
    def __init__(self,root,mode,code='',bind='0.0.0.0',port=0,advertise=None,bridge_factory=GameBridge,autopunch=False,selection_sync=True,skip_native_preflight=False):
        self.root=pathlib.Path(root);self.root.mkdir(parents=True,exist_ok=True);self.slot=0 if mode=='host' else 1
        self.sock=socket.socket(socket.AF_INET,socket.SOCK_DGRAM);self.sock.bind((bind,port));self.sock.setblocking(False)
        self.logical_port=self.sock.getsockname()[1];self.autopunch=False
        self.selection=None;self.enable_selection=selection_sync;self.remote_selection=None
        self.fighter_test=None;self.remote_fighter=None;self.live=None;self.remote_live=None
        self.remote_closed=False
        self.peer=None;self.remote_nonce=None;self.nonce=secrets.token_hex(8);self.seq=0;self.seen=set();self.high=-1;self.last_received=None
        self.key=secrets.token_bytes(16);self.code='';self.advertised_endpoint=None;self.phase='preparing';self.remote_phase='';self.remote_proof=None;self.native=None;self.local_proof=None
        self.closed=False;self.error=None;self.next_send=0;self.next_status=0;self.started=time.monotonic();self.last_tick=0;self.prepared=False;self.route_started=False;self.vs_ready=False;self.preflight_done=False;self.metrics={}
        if self.slot==0:
            relay=None
            if autopunch:
                config=settings(self.root);relay=ipv4_endpoint(config['relay_host'],config['relay_port'])
                public=discover_public_endpoint(self.sock,ipv4_endpoint(config['stun_host'],config['stun_port']));advertise=public[0]
                self.sock=AutoPunchSocket(self.sock,relay);self.autopunch=True
            elif advertise is None:
                # Route selection uses no game/Steam server; the UDP connect sends no datagram.
                with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as probe:
                    try:probe.connect(('192.0.2.1',9));advertise=probe.getsockname()[0]
                    except OSError:advertise='127.0.0.1'
            ipaddress.IPv4Address(advertise);self.advertised_endpoint=(advertise,self.logical_port);self.code=encode_code(*self.advertised_endpoint,self.key,relay=relay)
        else:
            self.peer,self.key,relay=decode_connection_code(code)
            if relay:self.sock=AutoPunchSocket(self.sock,relay);self.autopunch=True
        self.bridge=bridge_factory(self.root,self.slot,hashlib.sha256(self.key).hexdigest()[:24]);self.bridge.begin('peer_prepare')
        self.write_status()
    def write_status(self):
        data=dict(protocol=PROTOCOL,state=self.phase,slot=self.slot,code=self.code,pid=os.getpid(),peer_connected=self.remote_nonce is not None,
                  local_vs_ready=self.vs_ready,remote_vs_ready=self.remote_phase=='vs_ready',input_authority_enforced=bool(self.live and self.live.started),live_netplay=bool(self.live and self.live.state=="playing"),autopunch=self.autopunch,
                  message=self.error or ('Use the panel selection controls. After both players are Ready, choose Live keyboard play on both PCs.' if self.enable_selection else 'Stop at character selection; network fighting is not enabled.'))
        if self.selection:data.update(selection_state=self.selection.state,match_setup_ready=self.selection.ready,selection_authority_enforced=self.selection.catalog is not None)
        if self.autopunch:data.update(relay_endpoint=self.sock.relay,autopunch_registrations=self.sock.registrations,autopunch_mapping_responses=self.sock.mapping_responses,autopunch_punches=self.sock.punches)
        if self.native:data.update(frame=self.native.frame,rollback_advances=self.native.report.rollback_advances,desyncs=self.native.report.desyncs)
        else:data.update(self.metrics)
        if self.live and self.live.report:
            for name in ('frame','rollback_advances','desyncs','first_desync','input_device'):data[name]=self.live.report.get(name)
            data['live_state']=self.live.state
        if self.closed:data.update(input_authority_enforced=False,live_netplay=False)
        if self.closed and self.live and self.live.finished:data['message']='Match completed with matching final checksums. Local controls restored; return to the main menu and reconnect for another match.'
        data['advertised_endpoint']=self.advertised_endpoint;data['peer_endpoint']=self.peer
        atomic_json(self.root/'peer-status.json',data)
        summary='Connection test: '+self.phase+' | Player '+str(self.slot+1)+'\n'
        summary+=('AutoPunch enabled (direct UDP hole punching).' if self.autopunch else 'Direct / LAN / Radmin mode.')+'\n'
        if self.autopunch:summary+='Relay mappings: '+str(self.sock.mapping_responses)+'; punch packets: '+str(self.sock.punches)+'\n'
        if self.advertised_endpoint:summary+='Hosting at '+self.advertised_endpoint[0]+':'+str(self.advertised_endpoint[1])+'\n'
        elif self.peer:summary+='Connecting to '+self.peer[0]+':'+str(self.peer[1])+'\n'
        if self.remote_nonce:summary+='Peer authenticated. '
        if self.vs_ready:summary+='Local VS selection is open. '
        if self.native or self.metrics:summary+='Rollback frames: '+str(data.get('rollback_advances',0))+'; desyncs: '+str(data.get('desyncs',0))+'. '
        summary+='\n'+data['message']
        # Readers may briefly hold a file open without delete sharing.
        for name,text in [('peer-summary.txt',summary),('peer-code.txt',self.code if self.phase not in ('closed','error') else '')]:
            try:(self.root/name).write_text(text,encoding='utf-8')
            except PermissionError:pass
    def send(self,kind,payload=None):
        if self.peer is None:return
        self.seq+=1
        body=json.dumps(dict(v=PROTOCOL,build=BUILD,slot=self.slot,port=self.logical_port,nonce=self.nonce,to=self.remote_nonce,seq=self.seq,kind=kind,payload=payload),sort_keys=True,separators=(',',':')).encode()
        packet=hmac.new(self.key,body,hashlib.sha256).digest()+body
        if len(packet)>65000:raise RuntimeError('Peer datagram exceeds the UDP payload limit')
        try:self.sock.sendto(packet,self.peer)
        except (BlockingIOError,ConnectionResetError):pass
    def read(self):
        for _ in range(256):
            try:data,endpoint=self.sock.recvfrom(65535)
            except (BlockingIOError,ConnectionResetError):return
            if len(data)<32 or len(data)>65000 or (not self.autopunch and self.peer is not None and endpoint!=self.peer):continue
            tag,body=data[:32],data[32:]
            if not hmac.compare_digest(tag,hmac.new(self.key,body,hashlib.sha256).digest()):continue
            try:m=json.loads(body)
            except (ValueError,UnicodeError):continue
            if not isinstance(m,dict) or m.get('v')!=PROTOCOL or m.get('slot')!=1-self.slot:continue
            if m.get('build')!=BUILD:raise RuntimeError('Peer build/protocol differs; install the same launcher package')
            nonce=m.get('nonce');seq=m.get('seq');kind=m.get('kind')
            if not isinstance(nonce,str) or len(nonce)!=16 or type(seq) is not int or seq<1:continue
            first=self.remote_nonce is None
            if first:
                if kind!='hello' or (self.slot==1 and m.get('to')!=self.nonce):continue
            elif nonce!=self.remote_nonce:continue
            if m.get('to') not in (None,self.nonce) or seq in self.seen or seq<self.high-512:continue
            if self.autopunch:
                internal=m.get('port')
                if type(internal) is not int or not 1<=internal<=65535:continue
                if self.peer is not None and (endpoint[0],internal)!=self.peer:continue
                endpoint=self.sock.confirm_authenticated_peer(endpoint,internal)
            if first:
                self.peer=endpoint;self.remote_nonce=nonce
            self.high=max(self.high,seq);self.seen.add(seq);self.seen={n for n in self.seen if n>=self.high-512};self.last_received=time.monotonic()
            payload=m.get('payload')
            if kind=='close':
                reason=payload.get('error') if isinstance(payload,dict) else None
                if isinstance(reason,str):
                    reason=reason[:1024];atomic_json(self.root/'peer-end.json',dict(error=reason,session_id=self.bridge.session))
                if reason:raise RuntimeError('Peer ended the connection: '+reason)
                if isinstance(payload,dict) and payload.get('completed'):
                    if not self.live or not self.live.completion or payload.get('completion')!=self.live.completion:raise RuntimeError('Peer claimed an unmatched match completion')
                    self.live.finished=True;self.live.state='completed'
                self.remote_closed=True;return
            if kind=='state':
                if not isinstance(payload,dict):continue
                phase=payload.get('phase')
                if phase not in ('preparing','ready','checking','verified','opening_vs','vs_ready'):continue
                self.remote_phase=phase
                proof=payload.get('proof')
                if isinstance(proof,dict) and proof.get('frame')==120 and type(proof.get('value')) is int:self.remote_proof=proof
            if kind=='gekko' and self.native is not None:
                if not isinstance(payload,str) or len(payload)>81000:continue
                try:packet=base64.b64decode(payload,validate=True)
                except ValueError:continue
                self.native.receive(packet)
            if kind=='selection':
                if payload is not None and not isinstance(payload,dict):raise RuntimeError('Invalid peer selection payload')
                self.remote_selection=payload
            if kind=='fighter_test':self.remote_fighter=payload
            if kind=='live_state':self.remote_live=payload
            if kind=='live_gekko' and self.live:self.live.receive(payload)
    def step(self):
        if self.closed:return
        now=time.monotonic();self.read()
        if self.remote_closed:self.close();return
        if self.last_received is not None and now-self.last_received>12:raise RuntimeError('Peer timed out; the connection has been stopped')
        if now-self.started>90 and self.phase!='vs_ready':raise RuntimeError('Connection setup timed out')
        if not self.prepared:
            if self.bridge.poll()=='peer_prepared':self.prepared=True;self.phase='ready'
        if self.prepared and not self.preflight_done and self.remote_phase in ('ready','checking','verified','opening_vs','vs_ready') and self.native is None and not self.route_started:
            if self.skip_native_preflight:
                # Radmin's authenticated direct UDP handshake is the transport preflight.
                # The optional synthetic native peer library is not part of the game package.
                self.bridge.begin('open_local_vs');self.route_started=True;self.phase='opening_vs'
            else:
                self.native=SyntheticPeer(self.root/'MetalGekko.dll',self.slot);self.phase='checking'
        if self.native and now-self.last_tick>=1/60:
            self.last_tick=now;self.native.tick()
            for packet in self.native.packets():self.send('gekko',base64.b64encode(packet).decode())
            if self.native.frame>=144:
                self.local_proof=dict(frame=120,value=self.native.history[120]);self.phase='verified' if not self.route_started else self.phase
        if self.local_proof and self.remote_proof and not self.route_started:
            if self.local_proof!=self.remote_proof:raise RuntimeError('Peer input preflight checksums differ')
            self.bridge.begin('open_local_vs');self.route_started=True;self.phase='opening_vs'
        if self.route_started and not self.vs_ready:
            if self.bridge.poll()=='vs_ready':self.vs_ready=True;self.phase='vs_ready'
        if self.native and self.vs_ready and self.remote_phase=='vs_ready':
            self.metrics=dict(frame=self.native.frame,rollback_advances=self.native.report.rollback_advances,desyncs=self.native.report.desyncs)
            self.native.close();self.native=None;self.preflight_done=True
        if self.skip_native_preflight and self.vs_ready and self.remote_phase=='vs_ready':
            self.preflight_done=True
        if self.preflight_done and self.enable_selection:
            if self.selection is None:self.selection=SelectionSync(self.root,self.slot,self.bridge,atomic_json)
            self.selection.step(self.remote_selection)
            if self.selection.ready:
                if FighterInputTest is not None and self.fighter_test is None:
                    self.fighter_test=FighterInputTest(self.root,self.slot,self.bridge,self.selection,atomic_json)
                if self.fighter_test is not None and not (self.live and self.live.ready):
                    self.fighter_test.step(self.remote_fighter)
                if self.live is None:self.live=LiveFighter(self.root,self.slot,self.bridge,self.selection,atomic_json)
                if (self.live.ready and isinstance(self.remote_fighter,dict) and self.remote_fighter.get('ready') is True) or (self.fighter_test is not None and self.fighter_test.ready and isinstance(self.remote_live,dict) and self.remote_live.get('ready') is True):
                    raise RuntimeError('Both PCs must choose the same live input option; reconnect and retry')
                if self.fighter_test is None or not self.fighter_test.ready:
                    self.live.step(self.remote_live)
                for packet in self.live.packets():self.send('live_gekko',packet)
                if self.live.finished:self.close();return
        if now>=self.next_send:
            self.next_send=now+.15
            self.send('hello')
            if self.remote_nonce:self.send('state',dict(phase=self.phase,proof=self.local_proof))
            if self.selection:self.send('selection',self.selection.wire())
            if self.fighter_test:self.send('fighter_test',self.fighter_test.wire())
            if self.live:self.send('live_state',self.live.wire())
        if now>=self.next_status:self.next_status=now+.25;self.write_status()
    def close(self,error=None):
        if self.closed:return
        self.error=error
        try:
            for _ in range(3):self.send('close',dict(error=error,completed=bool(self.live and self.live.finished),completion=self.live.completion if self.live else None))
        finally:
            if self.live:self.live.close()
            if self.selection:self.selection.close()
            if self.native:self.native.close()
            self.sock.close();self.closed=True;self.phase='error' if error else 'closed';self.write_status()

def main():
    p=argparse.ArgumentParser();p.add_argument('--root',type=pathlib.Path,default=pathlib.Path(__file__).resolve().parent);p.add_argument('--request',type=pathlib.Path);p.add_argument('--mode',choices=['host','join']);p.add_argument('--code',default='');p.add_argument('--bind',default='0.0.0.0');p.add_argument('--port',type=int,default=0);p.add_argument('--advertise');p.add_argument('--autopunch',action='store_true');p.add_argument('--preflight-only',action='store_true');p.add_argument('--skip-native-preflight',action='store_true');args=p.parse_args()
    root=args.root.resolve();root.mkdir(parents=True,exist_ok=True)
    # This runtime is Windows-only; keeping the module importable elsewhere
    # allows CI to validate its connection-code protocol without game DLLs.
    if msvcrt is None:
        raise SystemExit('peer_session requires Windows')
    # Windows byte-range lock is released on process exit, including crashes.
    lock=open(root/'peer-session.lock','a+b');lock.seek(0);lock.write(b'0');lock.flush();lock.seek(0)
    try:msvcrt.locking(lock.fileno(),msvcrt.LK_NBLCK,1)
    except OSError:
        lock.close()
        raise SystemExit('A peer session is already running in this launcher folder')
    requested_address=None
    if args.request:
        request=json.loads(args.request.read_text());args.mode=request.get('mode');args.code=request.get('code','')
        args.autopunch=request.get('autopunch',False) is True
        address=request.get('host_address','').strip()
        if args.mode=='host' and address:requested_address=address
        if args.request.parent.resolve()==root and args.request.name.startswith('peer-request-'):args.request.unlink()
    if args.mode not in ('host','join'):raise SystemExit('Choose host or join')
    stop=root/'peer-stop.json'
    if stop.exists():stop.unlink()
    session=None
    try:
        if requested_address and not args.autopunch:
            address=str(ipaddress.IPv4Address(requested_address));args.advertise=address;args.bind=address
        if args.mode=='host' and args.autopunch:
            atomic_json(root/'peer-status.json',dict(state='discovering_public_endpoint',autopunch=True,live_netplay=False))
            (root/'peer-summary.txt').write_text('AutoPunch: discovering the public endpoint...',encoding='utf-8');(root/'peer-code.txt').write_text('')
        session=Session(root,args.mode,args.code,args.bind,args.port,args.advertise,autopunch=args.autopunch,selection_sync=not args.preflight_only,skip_native_preflight=args.skip_native_preflight)
        while not session.closed:
            if stop.exists():break
            session.step();time.sleep(.002)
    except Exception as e:
        if session:session.close(str(e))
        else:
            hint='Retry AutoPunch or switch to direct/Radmin mode.' if args.autopunch else 'For Radmin, use this PC\'s Radmin IPv4, not the other PC\'s address.'
            message='Could not start connection test: '+str(e)+'\n'+hint
            atomic_json(root/'peer-status.json',dict(state='error',message=message,live_netplay=False))
            (root/'peer-summary.txt').write_text(message,encoding='utf-8');(root/'peer-code.txt').write_text('')
        raise
    finally:
        if session:session.close()
        lock.close()
if __name__=='__main__':main()







