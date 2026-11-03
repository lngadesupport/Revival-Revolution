REVIVAL REVOLUTION — OFFLINE BOOTSTRAP SCRIPTS

Place the three original offline bootstrap files directly in this folder:

  offline_launcher.py
  inject_offline.py
  inject_overlay.py

Do not rename the files and do not place them in separate subfolders.

The launcher build is configured to copy every file in OfflineScripts to the build
output and published distribution, preserving this folder. The launcher searches
this folder automatically.

IMPORTANT:
- Add the original, working scripts from the trusted game distribution.
- Do not use placeholder or rewritten scripts.
- Do not include game binaries, proprietary game assets, or credentials here.
- Before publishing, verify that all three files are present in the final output:
  OfflineScripts/offline_launcher.py
  OfflineScripts/inject_offline.py
  OfflineScripts/inject_overlay.py
