"""ctypes binding and deterministic synthetic peer preflight (not game state)."""
import ctypes as C
import struct

Save=C.CFUNCTYPE(C.c_int,C.c_void_p,C.c_uint32,C.POINTER(C.c_uint32),C.POINTER(C.c_uint32),C.c_int,C.c_void_p)
Load=C.CFUNCTYPE(C.c_int,C.c_void_p,C.c_uint32,C.c_int,C.c_void_p)
Advance=C.CFUNCTYPE(C.c_int,C.c_void_p,C.c_uint32,C.c_int,C.c_int,C.c_void_p)
class Callbacks(C.Structure):
    _fields_=[('save',Save),('load',Load),('advance',Advance)]
class Report(C.Structure):
    _fields_=[(n,C.c_int) for n in ('saves','loads','advances','rollback_advances','desyncs','first_desync','callback_error')]

class SyntheticPeer:
    def __init__(self,path,slot):
        self.dll=C.CDLL(str(path));self.slot=slot;self.frame=0;self.value=42;self.history={0:42};self.error=None
        d=self.dll
        d.mg_peer_create.argtypes=[C.c_int,C.POINTER(Callbacks),C.c_void_p,C.c_uint32];d.mg_peer_create.restype=C.c_void_p
        d.mg_peer_destroy.argtypes=[C.c_void_p]
        d.mg_peer_receive.argtypes=[C.c_void_p,C.c_void_p,C.c_uint32]
        d.mg_peer_send.argtypes=[C.c_void_p,C.c_void_p,C.c_uint32]
        d.mg_peer_tick.argtypes=[C.c_void_p,C.c_uint32,C.POINTER(Report),C.POINTER(C.c_int)]
        def save(dst,cap,length,checksum,frame,user):
            try:
                if cap<8:return 0
                C.memmove(dst,struct.pack('<II',self.frame,self.value),8);length[0]=8;checksum[0]=(self.frame*16777619)^self.value;return 1
            except Exception as e:self.error=str(e);return 0
        def load(src,length,frame,user):
            try:
                if length!=8:return 0
                self.frame,self.value=struct.unpack('<II',C.string_at(src,8));return 1
            except Exception as e:self.error=str(e);return 0
        def advance(src,length,frame,rollback,user):
            try:
                if length!=8:return 0
                p1,p2=struct.unpack('<II',C.string_at(src,8))
                self.value=(self.value*1664525+1013904223+p1*17+p2*257)&0xffffffff
                self.frame+=1;self.history[self.frame]=self.value;return 1
            except Exception as e:self.error=str(e);return 0
        self.callbacks=Callbacks(Save(save),Load(load),Advance(advance))
        self.handle=d.mg_peer_create(slot,C.byref(self.callbacks),None,8)
        if not self.handle:raise RuntimeError('Cannot create native peer session')
        self.report=Report();self.status=C.c_int();self.buffer=C.create_string_buffer(60000)
    def tick(self):
        buttons=((self.frame//3+self.slot*5)%16)+1
        rc=self.dll.mg_peer_tick(self.handle,buttons,C.byref(self.report),C.byref(self.status))
        if rc or self.report.callback_error or self.report.desyncs or self.status.value==2:
            raise RuntimeError('Gekko peer failure rc=%s desyncs=%s callback=%s'%(rc,self.report.desyncs,self.error))
    def receive(self,packet):
        if self.dll.mg_peer_receive(self.handle,packet,len(packet)):raise RuntimeError('Native packet queue rejected input')
    def packets(self):
        for _ in range(256):
            n=self.dll.mg_peer_send(self.handle,self.buffer,len(self.buffer))
            if n<0:raise RuntimeError('Native packet exceeds transport limit')
            if n==0:return
            yield self.buffer.raw[:n]
    def close(self):
        if self.handle:
            self.dll.mg_peer_destroy(self.handle);self.handle=None
            # ctypes does not unload DLLs automatically on Windows.
            import _ctypes
            _ctypes.FreeLibrary(self.dll._handle);self.dll=None
