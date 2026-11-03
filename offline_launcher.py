import ctypes as c
import ctypes.wintypes as w
import pathlib,subprocess,sys,time,os,json
ROOT=pathlib.Path(__file__).resolve().parent
DEFAULT_GAME=r'Z:\SteamLibrary\steamapps\common\Metal Revolution'
class PROCESS(c.Structure):
 _fields_=[('size',w.DWORD),('usage',w.DWORD),('pid',w.DWORD),('heap',c.c_size_t),('module',w.DWORD),('threads',w.DWORD),('parent',w.DWORD),('priority',w.LONG),('flags',w.DWORD),('exe',w.WCHAR*260)]
k=c.WinDLL('kernel32',use_last_error=True)
k.CreateToolhelp32Snapshot.argtypes=[w.DWORD,w.DWORD];k.CreateToolhelp32Snapshot.restype=w.HANDLE
k.Process32FirstW.argtypes=[w.HANDLE,c.POINTER(PROCESS)];k.Process32NextW.argtypes=[w.HANDLE,c.POINTER(PROCESS)]
k.OpenProcess.argtypes=[w.DWORD,w.BOOL,w.DWORD];k.OpenProcess.restype=w.HANDLE
k.QueryFullProcessImageNameW.argtypes=[w.HANDLE,w.DWORD,w.LPWSTR,c.POINTER(w.DWORD)]
k.CloseHandle.argtypes=[w.HANDLE]
def game_pid(game):
 snap=k.CreateToolhelp32Snapshot(2,0);entry=PROCESS();entry.size=c.sizeof(entry);ok=k.Process32FirstW(snap,c.byref(entry));found=[]
 while ok:
  if entry.exe.lower()=='ufo.exe':
   h=k.OpenProcess(0x1000,False,entry.pid)
   if h:
    buf=c.create_unicode_buffer(32768);length=w.DWORD(len(buf))
    if k.QueryFullProcessImageNameW(h,0,buf,c.byref(length)) and os.path.normcase(buf.value)==os.path.normcase(str(game/'ufo.exe')):found.append(entry.pid)
    k.CloseHandle(h)
  ok=k.Process32NextW(snap,c.byref(entry))
 k.CloseHandle(snap)
 if len(found)>1:raise RuntimeError('More than one game is running. Close duplicate game sessions and try again.')
 return found[0] if found else None
def main():
 print('Metal Revolution - Offline Launcher\n')
 config=ROOT/'launcher-config.json'
 game=pathlib.Path(json.loads(config.read_text()).get('game_folder',DEFAULT_GAME) if config.exists() else DEFAULT_GAME)
 if not (game/'ufo.exe').is_file():raise RuntimeError('Game not found. Edit game_folder in launcher-config.json.')
 for name in ['inject_offline.py','MetalOfflineBridgeImGuiV3.dll']:
  if not (ROOT/name).exists():raise RuntimeError('Missing package file: '+name)
 pid=game_pid(game)
 if not pid:
  subprocess.Popen([str(game/'ufo.exe'),'-screen-fullscreen','1'],cwd=game);print('Game started directly in fullscreen mode; waiting for the game process.')
  for _ in range(100):
   pid=game_pid(game)
   if pid:break
   time.sleep(.1)
 if not pid:raise RuntimeError('The game did not start.')
 print('Preparing built-in offline mode. Leave this launcher open during startup.')
 status=ROOT/'offline-status.txt'
 if status.exists():status.unlink()
 # Wait for the protected Mono runtime and its managed game assembly to load.
 for attempt in range(300):
  result=subprocess.run([sys.executable,str(ROOT/'inject_offline.py'),str(pid)],cwd=ROOT,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
  if result.returncode==0:break
  if 'Mono runtime not loaded' in result.stdout or 'Game runtime not ready' in result.stdout:time.sleep(1);continue
  (ROOT/'launcher-error.txt').write_text(result.stdout)
  raise RuntimeError('Offline helper could not attach. See launcher-error.txt. If you launched the game as administrator, run this launcher as administrator too.')
 else:raise RuntimeError('Game runtime did not become ready within five minutes.')
 last=''
 for _ in range(240):
  if status.exists():
   line=status.read_text(errors='replace')
   if line!=last:print(line);last=line
   if 'READY:' in line:
    bridge=subprocess.run([sys.executable,str(ROOT/'inject_offline.py'),str(pid),'--netplay'],cwd=ROOT,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    if bridge.returncode:raise RuntimeError('The netplay development bridge could not attach: '+bridge.stdout)
    overlay=subprocess.run([sys.executable,str(ROOT/'inject_overlay.py'),str(pid)],cwd=ROOT,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    print(overlay.stdout)
    if overlay.returncode:raise RuntimeError('Offline mode is ready, but the overlay could not attach. See launcher.log.')
    return
   if 'ERROR ' in line:raise RuntimeError(line)
  if not game_pid(game):raise RuntimeError('The game closed during startup.')
  time.sleep(.5)
 raise RuntimeError('Startup timed out. The game may still be loading; see offline-status.txt.')
if __name__=='__main__':
 try:main()
 except Exception as e:print('\nERROR:',e);sys.exit(1)

