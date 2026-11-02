"""AutoPunch wire-protocol adapter for our own UDP socket.

Adapted from delthas/autopunch (MIT), commit
db0b962a8bed74179997e755e851f99e885a10d1, inject.c and relay.go.
Registration: internal port (2 bytes). Mapping request: port + peer IPv4/port.
Relay response: peer internal port + mapped port + IPv4. Punch: one zero byte.
Game/handshake datagrams still require the session's independent HMAC.
"""
import ipaddress,json,pathlib,secrets,socket,struct,time
COOKIE=0x2112A442

def ipv4_endpoint(host,port):
    if type(port) is not int or not 1<=port<=65535:raise ValueError('Invalid UDP service port')
    return socket.getaddrinfo(host,port,socket.AF_INET,socket.SOCK_DGRAM)[0][4]

def parse_stun(packet,transaction):
    if len(packet)<20:return None
    kind,length,cookie=struct.unpack('!HHI',packet[:8])
    if kind!=0x101 or cookie!=COOKIE or packet[8:20]!=transaction or length%4 or len(packet)!=20+length:return None
    offset=20;mapped=None
    while offset+4<=len(packet):
        attr,size=struct.unpack('!HH',packet[offset:offset+4]);offset+=4
        if offset+size>len(packet):return None
        value=packet[offset:offset+size];offset+=(size+3)&~3
        if attr in (0x0020,0x0001) and size==8 and value[1]==1:
            port=struct.unpack('!H',value[2:4])[0];address=struct.unpack('!I',value[4:8])[0]
            if attr==0x0020:port^=COOKIE>>16;address^=COOKIE
            endpoint=(socket.inet_ntoa(struct.pack('!I',address)),port)
            if port:
                if attr==0x0020:return endpoint
                mapped=endpoint
    return mapped

def discover_public_endpoint(sock,server,timeout=6):
    transaction=secrets.token_bytes(12);request=struct.pack('!HHI',1,0,COOKIE)+transaction
    deadline=time.monotonic()+timeout;next_send=0
    try:
        while time.monotonic()<deadline:
            now=time.monotonic()
            if now>=next_send:sock.sendto(request,server);next_send=now+.5
            sock.settimeout(min(.2,max(.001,deadline-now)))
            try:packet,source=sock.recvfrom(2048)
            except (socket.timeout,ConnectionResetError):continue
            if source!=server:continue
            endpoint=parse_stun(packet,transaction)
            if endpoint:
                address=ipaddress.IPv4Address(endpoint[0])
                if address.is_unspecified or address.is_multicast:continue
                return endpoint
    finally:sock.setblocking(False)
    raise RuntimeError('Public endpoint discovery timed out. Retry AutoPunch or use direct/Radmin mode.')

def settings(root):
    config=dict(relay_host='delthas.fr',relay_port=14763,stun_host='stun.cloudflare.com',stun_port=3478)
    path=pathlib.Path(root)/'autopunch-config.json'
    if path.exists():config.update(json.loads(path.read_text(encoding='utf-8-sig')))
    return config

class AutoPunchSocket:
    def __init__(self,sock,relay):
        self.sock=sock;self.relay=relay;self.port=sock.getsockname()[1];self.mappings={};self.requests={};self.next_register=0
        self.registrations=0;self.mapping_responses=0;self.punches=0
    def _send(self,data,endpoint):
        try:return self.sock.sendto(data,endpoint)
        except (BlockingIOError,ConnectionResetError):return 0
    def pump(self):
        now=time.monotonic()
        if now>=self.next_register:
            self.next_register=now+1;self._send(struct.pack('!H',self.port),self.relay);self.registrations+=1
        for logical,(wire,updated,next_punch) in list(self.mappings.items()):
            if now-updated>12:del self.mappings[logical];continue
            if now>=next_punch:
                self._send(b'\0',wire);self.punches+=1;self.mappings[logical]=(wire,updated,now+.5)
    def sendto(self,data,endpoint):
        self.pump();now=time.monotonic()
        if now>=self.requests.get(endpoint,0):
            if len(self.requests)>=32 and endpoint not in self.requests:self.requests.pop(next(iter(self.requests)))
            self.requests[endpoint]=now+.5
            self._send(struct.pack('!H',self.port)+socket.inet_aton(endpoint[0])+struct.pack('!H',endpoint[1]),self.relay)
        mapping=self.mappings.get(endpoint);return self._send(data,mapping[0] if mapping else endpoint)
    def recvfrom(self,capacity):
        self.pump()
        for _ in range(128):
            packet,endpoint=self.sock.recvfrom(capacity)
            if endpoint==self.relay:
                if len(packet)==8:
                    internal,mapped=struct.unpack('!HH',packet[:4]);address=socket.inet_ntoa(packet[4:8]);ip=ipaddress.IPv4Address(address)
                    if internal and mapped and not ip.is_multicast and not ip.is_unspecified:
                        logical=(address,internal)
                        if len(self.mappings)<32 or logical in self.mappings:
                            self.mappings[logical]=((address,mapped),time.monotonic(),0);self.mapping_responses+=1
                continue
            if packet==b'\0':continue
            for logical,(wire,updated,next_punch) in self.mappings.items():
                if wire==endpoint:
                    self.mappings[logical]=(wire,time.monotonic(),next_punch);return packet,logical
            return packet,endpoint
        raise BlockingIOError()
    def confirm_authenticated_peer(self,source,internal_port):
        logical=(source[0],internal_port)
        if source!=logical and (logical in self.mappings or len(self.mappings)<32):self.mappings[logical]=(source,time.monotonic(),0)
        return logical
    def close(self):self.sock.close()
