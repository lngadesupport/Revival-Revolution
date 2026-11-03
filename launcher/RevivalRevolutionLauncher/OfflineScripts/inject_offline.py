import ctypes as c,ctypes.wintypes as w,struct,pathlib,json,sys,time
k=c.WinDLL('kernel32',use_last_error=True)
def api(name,args,ret):
 f=getattr(k,name);f.argtypes=args;f.restype=ret;return f
Open=api('OpenProcess',[w.DWORD,w.BOOL,w.DWORD],w.HANDLE)
Read=api('ReadProcessMemory',[w.HANDLE,c.c_void_p,c.c_void_p,c.c_size_t,c.POINTER(c.c_size_t)],w.BOOL)
Write=api('WriteProcessMemory',[w.HANDLE,c.c_void_p,c.c_void_p,c.c_size_t,c.POINTER(c.c_size_t)],w.BOOL)
Alloc=api('VirtualAllocEx',[w.HANDLE,c.c_void_p,c.c_size_t,w.DWORD,w.DWORD],c.c_void_p)
Protect=api('VirtualProtectEx',[w.HANDLE,c.c_void_p,c.c_size_t,w.DWORD,c.POINTER(w.DWORD)],w.BOOL)
Thread=api('CreateRemoteThread',[w.HANDLE,c.c_void_p,c.c_size_t,c.c_void_p,c.c_void_p,w.DWORD,c.c_void_p],w.HANDLE)
Wait=api('WaitForSingleObject',[w.HANDLE,w.DWORD],w.DWORD)
Close=api('CloseHandle',[w.HANDLE],w.BOOL)
Snap=api('CreateToolhelp32Snapshot',[w.DWORD,w.DWORD],w.HANDLE)
class MOD(c.Structure):
 _fields_=[('size',w.DWORD),('mid',w.DWORD),('pid',w.DWORD),('gu',w.DWORD),('pu',w.DWORD),('base',c.c_void_p),('length',w.DWORD),('handle',w.HMODULE),('name',w.WCHAR*256),('path',w.WCHAR*260)]
First=api('Module32FirstW',[w.HANDLE,c.POINTER(MOD)],w.BOOL);Next=api('Module32NextW',[w.HANDLE,c.POINTER(MOD)],w.BOOL)
pid=int(sys.argv[1]);h=Open(0x43a,False,pid)
if not h:raise OSError(c.get_last_error())
def read(a,n):
 b=c.create_string_buffer(n);got=c.c_size_t();Read(h,a,b,n,c.byref(got));return b.raw[:got.value]
def write(a,d):
 got=c.c_size_t()
 if not Write(h,a,d,len(d),c.byref(got)) or got.value!=len(d):raise OSError(c.get_last_error())
def alloc(d,exec=False):
 a=Alloc(h,None,max(len(d),1),0x3000,0x40 if exec else 4)
 if not a:raise OSError(c.get_last_error())
 write(a,d);return a
s=Snap(0x18,pid);m=MOD();m.size=c.sizeof(m);ok=First(s,c.byref(m));base=0
while ok:
 if 'mono-2.0' in m.name.lower():base=m.base;break
 ok=Next(s,c.byref(m))
Close(s)
if not base:raise RuntimeError('Mono runtime not loaded')
pe=struct.unpack_from('<I',read(base,128),60)[0];hdr=read(base+pe,264);exrva,exsize=struct.unpack_from('<II',hdr,24+112);ex=read(base+exrva,40)
nf,nn,ft,nt,ot=struct.unpack_from('<IIIII',ex,20);func=read(base+ft,nf*4);names=read(base+nt,nn*4);ords=read(base+ot,nn*2);exports={}
for i in range(nn):
 nr=struct.unpack_from('<I',names,i*4)[0];name=read(base+nr,150).split(b'\0')[0].decode(errors='replace');ordinal=struct.unpack_from('<H',ords,i*2)[0];exports[name]=base+struct.unpack_from('<I',func,ordinal*4)[0]
root=0
p64=lambda n:struct.pack('<Q',n)
def call(name,*args,attach=True):
 result=alloc(bytes(8));code=b'\x48\x83\xec\x28'
 if root and attach:
  code+=b'\x48\xb9'+p64(root)+b'\x48\xb8'+p64(exports['mono_thread_attach'])+b'\xff\xd0\x48\x89\x44\x24\x20'
 for prefix,arg in zip([b'\x48\xb9',b'\x48\xba',b'\x49\xb8',b'\x49\xb9'],args):code+=prefix+p64(arg)
 code+=b'\x48\xb8'+p64(exports[name])+b'\xff\xd0\x48\xba'+p64(result)+b'\x48\x89\x02'
 if root and attach:code+=b'\x48\x8b\x4c\x24\x20\x48\xb8'+p64(exports['mono_thread_detach'])+b'\xff\xd0'
 code+=b'\x48\x83\xc4\x28\xc3'
 address=alloc(code,True);t=Thread(h,None,0,address,None,0,None)
 if not t:raise OSError(c.get_last_error())
 status=Wait(t,15000);Close(t)
 if status:raise RuntimeError(f'{name} timeout {status}')
 value=struct.unpack('<Q',read(result,8))[0];return value
root=call('mono_get_root_domain',attach=False)
if not root:raise RuntimeError('Game runtime not ready')
ready=call('mono_image_loaded',alloc(b'Assembly-CSharp\0'))
core=call('mono_image_loaded',alloc(b'UnityEngine.CoreModule\0'))
if not ready or not core:raise RuntimeError('Game runtime not ready')
path=alloc(str(pathlib.Path(__file__).parent.joinpath('MetalNetplayBridgeV8.dll' if '--netplay' in sys.argv else 'MetalOfflineBridgeImGuiV3.dll').resolve()).encode()+b'\0')
assembly=call('mono_domain_assembly_open',root,path)
if not assembly:raise RuntimeError('Helper assembly load failed')
img=call('mono_assembly_get_image',assembly)
cls=call('mono_class_from_name',img,alloc(b'\0'),alloc(b'MetalNetplayBridge\0' if '--netplay' in sys.argv else b'MetalOfflineBridge\0'))
method=call('mono_class_get_method_from_name',cls,alloc(b'Run\0'),0)
# Resolve the already loaded game assembly without loading another copy.
gameimg=call('mono_image_loaded',alloc(b'Assembly-CSharp\0'))
if not gameimg:gameimg=call('mono_image_loaded',alloc(b'Assembly-CSharp.dll\0'))
gamecls=call('mono_class_from_name',gameimg,alloc(b'MetalFury.Logic\0'),alloc(b'GameApp\0'))
update=call('mono_class_get_method_from_name',gamecls,alloc(b'Update\0'),0)
native=call('mono_compile_method',update)
if not native or not method:raise RuntimeError('Method resolution failed')
original=read(native,12)
work=pathlib.Path(__file__).parent
(work/'hook.txt').write_text(f'{native:x}\n{original.hex()}\n')
exc=alloc(bytes(8));stub=b'\x48\x83\xec\x28\x48\xb9'+p64(method)+b'\x48\x31\xd2\x4d\x31\xc0\x49\xb9'+p64(exc)+b'\x48\xb8'+p64(exports['mono_runtime_invoke'])+b'\xff\xd0\x48\x83\xc4\x28\xc3'
tramp=alloc(stub,True);old=w.DWORD()
if not Protect(h,native,12,0x40,c.byref(old)):raise OSError(c.get_last_error())
write(native,b'\x48\xb8'+p64(tramp)+b'\xff\xe0')
print('One-frame offline helper installed',flush=True)
for i in range(2400):
 if read(native,12)==original:break
 time.sleep(.1)
else:
 write(native,original);raise RuntimeError('Hook not invoked; restored original code')
Protect(h,native,12,old,c.byref(w.DWORD()))
print('Original game update restored; exception',read(exc,8).hex())
Close(h)



