"""Compare the bounded live-desync.json reports from the two game PCs."""
import argparse,json,pathlib

def differences(a,b,path='$',result=None):
    result=[] if result is None else result
    if len(result)>=40:return result
    if type(a)!=type(b):result.append(dict(path=path,left=a,right=b))
    elif isinstance(a,dict):
        for key in sorted(a.keys()|b.keys()):
            if key not in a or key not in b:result.append(dict(path=path+'.'+key,left=a.get(key),right=b.get(key)))
            else:differences(a[key],b[key],path+'.'+key,result)
            if len(result)>=40:break
    elif isinstance(a,list):
        if len(a)!=len(b):result.append(dict(path=path+'.length',left=len(a),right=len(b)))
        for i,(x,y) in enumerate(zip(a,b)):differences(x,y,path+'['+str(i)+']',result)
    elif a!=b:result.append(dict(path=path,left=a,right=b))
    return result

def compare(left,right):
    if left.get('session_id')!=right.get('session_id') or left.get('test_id')!=right.get('test_id'):raise ValueError('Reports belong to different sessions')
    frames=[{r['frame']:r for r in report['frames']} for report in (left,right)]
    shared=sorted(frames[0].keys()&frames[1].keys())
    mismatch=next((f for f in shared if frames[0][f]['checksum']!=frames[1][f]['checksum']),None)
    result=dict(shared_frames=len(shared),first_retained_checksum_mismatch=mismatch,reported_first_desync=[r.get('first_desync') for r in (left,right)])
    if mismatch is not None:
        a,b=[r[mismatch] for r in frames];parts=[r['state_text'].split('|rng:',1) for r in (a,b)]
        result['core_differences']=differences(json.loads(parts[0][0]),json.loads(parts[1][0]))
        result['rng_or_input_history_differs']=parts[0][1:]!=parts[1][1:]
        inputs=[{r['frame']:r for r in report.get('inputs',[])} for report in (left,right)]
        result['nearby_input_differences']=[dict(frame=f,left=inputs[0][f],right=inputs[1][f]) for f in sorted(inputs[0].keys()&inputs[1].keys()) if f<=mismatch+1 and inputs[0][f]!=inputs[1][f]][-16:]
    return result

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('left',type=pathlib.Path);parser.add_argument('right',type=pathlib.Path);args=parser.parse_args()
    print(json.dumps(compare(*[json.loads(p.read_text(encoding='utf-8-sig')) for p in (args.left,args.right)]),indent=2))
