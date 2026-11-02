"""Collect bounded desync reports without connection codes or local capabilities."""
import datetime,pathlib,zipfile
root=pathlib.Path(__file__).resolve().parent
names=('live-desync.json','live-fighter.json','fighter-load.json','selection-applied.json','peer-end.json')
archive=root/('MetalNetplay-Diagnostics-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S')+'.zip')
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
    for name in names:
        p=root/name
        if p.is_file():z.write(p,name)
print(archive)
