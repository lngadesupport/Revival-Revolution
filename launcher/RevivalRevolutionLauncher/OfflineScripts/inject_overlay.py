"""Load this package's overlay into the selected game's process."""
import ctypes as c,ctypes.wintypes as w,pathlib,sys,time
k=c.WinDLL('kernel32',use_last_error=True)
def api(n,a,r):f=getattr(k,n);f.argtypes=a;f.restype=r;return f
Open=api('OpenProcess',[w.DWORD,w.BOOL,w.DWORD],w.HANDLE);Alloc=api('VirtualAllocEx',[w.HANDLE,c.c_void_p,c.c_size_t,w.DWORD,w.DWORD],c.c_void_p)
Write=api('WriteProcessMemory',[w.HANDLE,c.c_void_p,c.c_void_p,c.c_size_t,c.POINTER(c.c_size_t)],w.BOOL)
Thread=api('CreateRemoteThread',[w.HANDLE,c.c_void_p,c.c_size_t,c.c_void_p,c.c_void_p,w.DWORD,c.c_void_p],w.HANDLE)
Wait=api('WaitForSingleObject',[w.HANDLE,w.DWORD],w.DWORD);Close=api('CloseHandle',[w.HANDLE],w.BOOL)
Module=api('GetModuleHandleW',[w.LPCWSTR],w.HMODULE);Proc=api('GetProcAddress',[w.HMODULE,c.c_char_p],c.c_void_p)
Owner=api('GetModuleHandleExW',[w.DWORD,c.c_void_p,c.POINTER(w.HMODULE)],w.BOOL)
Filename=api('GetModuleFileNameW',[w.HMODULE,w.LPWSTR,w.DWORD],w.DWORD)
Snap=api('CreateToolhelp32Snapshot',[w.DWORD,w.DWORD],w.HANDLE)
class MOD(c.Structure):
 _fields_=[('size',w.DWORD),('mid',w.DWORD),('pid',w.DWORD),('gu',w.DWORD),('pu',w.DWORD),('base',c.c_void_p),('length',w.DWORD),('handle',w.HMODULE),('name',w.WCHAR*256),('path',w.WCHAR*260)]
First=api('Module32FirstW',[w.HANDLE,c.POINTER(MOD)],w.BOOL);Next=api('Module32NextW',[w.HANDLE,c.POINTER(MOD)],w.BOOL)
def modules(pid):
 s=Snap(0x18,pid);m=MOD();m.size=c.sizeof(m);result={};ok=First(s,c.byref(m))
 while ok:result[m.name.lower()]=(m.base,m.path);ok=Next(s,c.byref(m))
 Close(s);return result
pid=int(sys.argv[1]);dll=pathlib.Path(__file__).resolve().parent/'MetalImGuiOverlay.dll'
if not dll.is_file():raise RuntimeError('Overlay DLL missing')
mods=modules(pid)
if dll.name.lower() in mods:
 if pathlib.Path(mods[dll.name.lower()][1]).resolve()!=dll.resolve():raise RuntimeError('An overlay from another package is already loaded. Restart the game to use this one.')
 print('Overlay already attached. Press F1.');sys.exit(0)
load=Proc(Module('kernel32.dll'),b'LoadLibraryW');owner=w.HMODULE()
if not Owner(6,load,c.byref(owner)):raise OSError(c.get_last_error())
name=c.create_unicode_buffer(32768);Filename(owner,name,len(name));remote=mods[pathlib.Path(name.value).name.lower()][0]+load-owner.value
h=Open(0x43a,False,pid)
if not h:raise OSError(c.get_last_error())
data=(str(dll)+'\0').encode('utf-16le');address=Alloc(h,None,len(data),0x3000,4);written=c.c_size_t()
if not address or not Write(h,address,data,len(data),c.byref(written)):raise OSError(c.get_last_error())
t=Thread(h,None,0,remote,address,0,None)
if not t:raise OSError(c.get_last_error())
if Wait(t,15000)!=0:raise RuntimeError('Overlay load timed out')
Close(t);Close(h)
if dll.name.lower() not in modules(pid):raise RuntimeError('Overlay failed to load')
print('In-game ImGui overlay attached. Press F1 to toggle.')
