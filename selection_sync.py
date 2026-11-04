"""Owned-slot selection snapshots and a proposal/accept/commit setup barrier."""
import hashlib,json,pathlib,secrets,time

# The roster can grow as community ports add characters, outfits and stages.
# Hard limits still protect the UDP protocol from unbounded/malicious catalogs.
MAX_CHARACTERS=4096
MAX_OUTFITS_PER_CHARACTER=1024
MAX_STAGES=4096
MAX_TOTAL_CATALOG_ENTRIES=65536
MAX_CONTENT_TOKEN_LENGTH=96
CATALOG_SCHEMA=2

def canonical(value):return json.dumps(value,sort_keys=True,separators=(',',':'))
def digest(value):return hashlib.sha256(value.encode('utf-8')).hexdigest()

def normalize_compatibility(value):
    if value is None:value={}
    if not isinstance(value,dict):raise RuntimeError('Invalid game content compatibility profile')
    game_build=value.get('game_build','')
    content_revision=value.get('content_revision','')
    content_hash=value.get('content_hash','')
    asset_pack_hash=value.get('asset_pack_hash','')
    bridge_protocol=value.get('bridge_protocol',1)
    for label,token in (('game_build',game_build),('content_revision',content_revision)):
        if not isinstance(token,str) or len(token)>MAX_CONTENT_TOKEN_LENGTH or any(ord(ch)<32 for ch in token):
            raise RuntimeError('Invalid content compatibility field: '+label)
    for label,token in (('content_hash',content_hash),('asset_pack_hash',asset_pack_hash)):
        if not isinstance(token,str) or (token and (len(token)!=64 or any(ch not in '0123456789abcdefABCDEF' for ch in token))):
            raise RuntimeError('Invalid content compatibility hash: '+label)
    if type(bridge_protocol) is not int or not 1<=bridge_protocol<=65535:
        raise RuntimeError('Invalid game bridge protocol version')
    return dict(game_build=game_build,content_revision=content_revision,
                content_hash=content_hash.lower(),asset_pack_hash=asset_pack_hash.lower(),
                bridge_protocol=bridge_protocol)

def catalog_fingerprint(characters,stages,compatibility):
    roster=sorted((item['id'],sorted(item['outfits'])) for item in characters)
    return digest(canonical(dict(schema=CATALOG_SCHEMA,characters=roster,
                                 stages=sorted(stages),compatibility=compatibility)))

class SelectionSync:
    def __init__(self,root,slot,bridge,atomic):
        self.root=pathlib.Path(root);self.slot=slot;self.bridge=bridge;self.atomic=atomic;self.catalog=None;self.catalog_hash='';self.local=None;self.remote=None
        self.revision=0;self.last_request='';self.proposal=None;self.proposal_hash='';self.accepted='';self.commit=False;self.applied='';self.last_preview='';self.remote_revision=-1;self.remote_owned=None
        self.action='selection_begin';self.state='opening';self.locked=False;self.next_lease=0;self.next_status=0;self.ready=False
        self.lease();bridge.begin(self.action)
    def lease(self):
        now=time.monotonic()
        if now>=self.next_lease:
            self.next_lease=now+.5
            self.atomic(self.root/'selection-lease.json',dict(session_id=self.bridge.session,target_pid=self.bridge.command['target_pid'] if self.bridge.command else int((self.root/'netplay-pid.txt').read_text())))
    def load_catalog(self):
        c=json.loads((self.root/'selection-catalog.json').read_text(encoding='utf-8-sig'))
        if not isinstance(c,dict):
            raise RuntimeError('Invalid selection catalog document')
        if c.get('session_id')!=self.bridge.session or c.get('local_slot')!=self.slot or c.get('pid')!=self.bridge.command['target_pid']:
            raise RuntimeError('Selection catalog belongs to a different game/session')
        characters=c.get('characters');stages=c.get('stages')
        if (not isinstance(characters,list) or not 1<=len(characters)<=MAX_CHARACTERS
                or not isinstance(stages,list) or not 1<=len(stages)<=MAX_STAGES
                or len(characters)+len(stages)>MAX_TOTAL_CATALOG_ENTRIES):
            raise RuntimeError('Invalid selection catalog')
        total_entries=len(characters)+len(stages)
        for item in characters:
            if (not isinstance(item,dict) or type(item.get('id')) is not int
                    or not 0<=item['id']<2**31
                    or not isinstance(item.get('outfits'),list)
                    or not 1<=len(item['outfits'])<=MAX_OUTFITS_PER_CHARACTER
                    or any(type(i) is not int or not 0<=i<2**31 for i in item['outfits'])
                    or len(set(item['outfits']))!=len(item['outfits'])):
                raise RuntimeError('Invalid character/outfit catalog')
            total_entries+=len(item['outfits'])
            if total_entries>MAX_TOTAL_CATALOG_ENTRIES:
                raise RuntimeError('Selection catalog exceeds the total entry limit')
            name=item.get('name','')
            if not isinstance(name,str) or len(name)>MAX_CONTENT_TOKEN_LENGTH:
                raise RuntimeError('Invalid character display name')
        if any(not isinstance(stage,dict) or type(stage.get('id')) is not int
               or not 0<=stage['id']<2**31 for stage in stages):
            raise RuntimeError('Invalid stage catalog')
        self.characters={item['id']:item for item in characters};self.stages={item['id'] for item in stages}
        if len(self.characters)!=len(characters) or len(self.stages)!=len(stages):raise RuntimeError('Duplicate selection catalog IDs')
        self.compatibility=normalize_compatibility(c.get('compatibility'))
        self.catalog=c
        self.catalog_hash=catalog_fingerprint(characters,self.stages,self.compatibility)
        self.local=dict(choice=dict(character=characters[0]['id'],costume=characters[0]['outfits'][0]),stage=stages[0]['id'] if self.slot==0 else -1,ready=False)
        self.revision+=1;self.state='choosing'
        (self.root/'selection-owner.txt').write_text(str(self.slot)+' '+self.bridge.session,encoding='utf-8')
    def validate_choice(self,choice):
        if not isinstance(choice,dict) or set(choice)!= {'character','costume'} or type(choice.get('character')) is not int or type(choice.get('costume')) is not int:raise RuntimeError('Invalid owned-slot selection')
        char=self.characters.get(choice['character'])
        if char is None or choice['costume'] not in char['outfits']:raise RuntimeError('Unknown character or outfit')
    def read_local(self):
        try:r=json.loads((self.root/'selection-local.json').read_text(encoding='utf-8-sig'))
        except (OSError,ValueError):return
        if not isinstance(r,dict) or r.get('session_id')!=self.bridge.session or r.get('id')==self.last_request:return
        request_id=r.get('id')
        if not isinstance(request_id,str) or not 1<=len(request_id)<=128:
            raise RuntimeError('Invalid selection request identifier')
        if request_id==self.last_request:return
        self.last_request=request_id
        if self.locked:return
        if 'ready' in r and type(r['ready']) is not bool:
            raise RuntimeError('Invalid local selection readiness')
        choice=dict(character=r.get('character'),costume=r.get('costume'));self.validate_choice(choice)
        stage=r.get('stage') if self.slot==0 else -1
        if self.slot==0 and (type(stage) is not int or stage not in self.stages):
            raise RuntimeError('Invalid host stage selection')
        local=dict(choice=choice,stage=stage,ready=r.get('ready') is True)
        if local!=self.local:self.local=local;self.revision+=1
    def receive(self,wire):
        if not self.catalog or wire is None:return
        if not isinstance(wire,dict) or wire.get('catalog')!=self.catalog_hash:
            raise RuntimeError('The peers have different character/outfit/stage catalogs')
        rev=wire.get('revision')
        if type(rev) is not int or not 0<=rev<2**31:
            raise RuntimeError('Invalid selection revision')
        if rev<self.remote_revision:return
        owned=wire.get('owned')
        if not isinstance(owned,dict) or set(owned)!= {'choice','stage','ready'} or type(owned['ready']) is not bool:
            raise RuntimeError('Invalid owned-slot readiness')
        self.validate_choice(owned.get('choice'))
        if self.slot==0:
            if owned['stage']!=-1:raise RuntimeError('Guest attempted to choose the host stage')
        elif type(owned['stage']) is not int or owned['stage'] not in self.stages:raise RuntimeError('Invalid host stage')
        if rev==self.remote_revision and self.remote is not None and wire!=self.remote:raise RuntimeError('A selection revision changed contents')
        if self.locked and self.remote_owned is not None and owned!=self.remote_owned:raise RuntimeError('Peer changed its selection after accepting the setup')
        self.remote=wire;self.remote_revision=rev;self.remote_owned=owned
    def wire(self):
        if self.catalog is None:return None
        return dict(catalog=self.catalog_hash,revision=self.revision,owned=self.local,proposal=self.proposal if self.slot==0 else None,proposal_hash=self.proposal_hash if self.slot==0 else '',accepted=self.accepted if self.slot==1 else '',commit=self.commit if self.slot==0 else False,applied=self.applied)
    def configuration(self,seed):
        choices=[None,None];choices[self.slot]=self.local['choice'];choices[1-self.slot]=self.remote_owned['choice'] if self.remote_owned else self.local['choice']
        stage=self.local['stage'] if self.slot==0 else self.remote_owned['stage'] if self.remote_owned else min(self.stages)
        return dict(player1=choices[0],player2=choices[1],stage=stage,seed=seed)
    def validate_proposal(self,proposal,hash_value):
        if not isinstance(proposal,dict) or set(proposal)!= {'player1','player2','stage','seed'} or type(proposal.get('seed')) is not int or not 1<=proposal['seed']<2**31:raise RuntimeError('Invalid shared match setup')
        if proposal!=self.configuration(proposal['seed']) or digest(canonical(proposal))!=hash_value:raise RuntimeError('Shared setup differs from owned selections')
    def step(self,remote_wire):
        self.lease()
        if self.action:
            result=self.bridge.poll()
            if result is not None:
                expected={'selection_begin':'selection_ready','selection_preview':'selection_previewed','selection_apply':'selection_applied'}[self.action]
                if result!=expected:raise RuntimeError('Unexpected game selection response: '+result)
                if self.action=='selection_begin':self.load_catalog()
                if self.action=='selection_apply':self.applied=self.proposal_hash;self.revision+=1
                self.action=None
        if self.catalog is None:return
        self.read_local();self.receive(remote_wire)
        if self.remote is not None:
            if self.slot==0:
                if self.remote.get('proposal') is not None or self.remote.get('proposal_hash') or self.remote.get('commit') is not False:raise RuntimeError('Guest attempted to issue a host setup proposal')
                if self.local['ready'] and self.remote_owned['ready'] and self.proposal is None:
                    self.proposal=self.configuration(secrets.randbelow(2**31-1)+1);self.proposal_hash=digest(canonical(self.proposal));self.locked=True;self.revision+=1;self.state='waiting_for_accept'
                if self.proposal and self.remote.get('accepted')==self.proposal_hash and not self.commit:
                    self.commit=True;self.revision+=1;self.state='applying'
            else:
                proposal=self.remote.get('proposal');proposed_hash=self.remote.get('proposal_hash')
                if proposal is not None:
                    if not self.local['ready'] or not self.remote_owned['ready']:raise RuntimeError('Host proposed a setup before both peers were ready')
                    self.validate_proposal(proposal,proposed_hash)
                    if self.proposal is not None and proposal!=self.proposal:raise RuntimeError('Host changed an accepted setup')
                    if not self.accepted:self.proposal=proposal;self.proposal_hash=proposed_hash;self.accepted=proposed_hash;self.locked=True;self.revision+=1;self.state='accepted'
                    if self.remote.get('commit') is True:self.commit=True;self.state='applying'
                elif self.remote.get('commit'):raise RuntimeError('Host committed without a setup proposal')
        if self.commit and not self.applied and not self.action:
            self.bridge.begin('selection_apply',setup_json=canonical(self.proposal),setup_hash=self.proposal_hash);self.action='selection_apply'
        elif not self.proposal and not self.action:
            preview=canonical(self.configuration(1))
            if preview!=self.last_preview:self.bridge.begin('selection_preview',setup_json=preview);self.action='selection_preview';self.last_preview=preview
        if self.applied and self.remote is not None and self.remote.get('applied')==self.applied:self.ready=True;self.state='match_setup_ready'
        if time.monotonic()>=self.next_status:
            self.next_status=time.monotonic()+.1
            local_name=self.characters[self.local['choice']['character']].get('name','Character')
            summary='Player '+str(self.slot+1)+': '+local_name+' | '+('Ready' if self.local['ready'] else 'Choosing')+'\n'
            if self.remote_owned:summary+='Remote player: '+self.characters[self.remote_owned['choice']['character']].get('name','Character')+' | '+('Ready' if self.remote_owned['ready'] else 'Choosing')+'\n'
            summary+='Setup: '+self.state+'\n'+('Shared character/outfit/stage/seed setup verified. Choose Live keyboard play on both PCs, or a diagnostic.' if self.ready else 'Choose your fighter and outfit in this panel. Host chooses the stage. Both players must press Ready.')
            (self.root/'selection-summary.txt').write_text(summary,encoding='utf-8');(self.root/'selection-locked.txt').write_text('true' if self.locked else 'false')
            (self.root/'selection-local-state.txt').write_text(self.bridge.session+' '+str(self.last_request or '-')+' '+str(self.local['choice']['character'])+' '+str(self.local['choice']['costume'])+' '+str(self.local['stage'])+' '+str(int(self.local['ready'])),encoding='utf-8')
            self.atomic(self.root/'selection-sync-status.json',dict(state=self.state,slot=self.slot,local=self.local,remote=self.remote_owned,setup_hash=self.proposal_hash,match_setup_ready=self.ready,live_netplay=False,catalog_schema=CATALOG_SCHEMA,catalog_hash=self.catalog_hash,compatibility=self.compatibility))
    def close(self):
        try:self.bridge.begin('selection_end')
        except (OSError,ValueError):pass
        (self.root/'selection-owner.txt').write_text('');(self.root/'selection-summary.txt').write_text('Selection session disconnected. Local controls will be restored.')

