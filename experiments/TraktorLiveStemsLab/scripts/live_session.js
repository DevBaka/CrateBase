// Private 4.5.1 experiment: factory/control callbacks only; native C owns every read.
const settings=LIVE_CONFIG;
const base=Process.getModuleByName('Traktor Pro 4.exe').base;
const dll=Module.load(settings.dll);
const createCache=new NativeFunction(dll.getExportByName('LabCreateCache'),'int',['pointer','uint64','uint']);
const registerSource=new NativeFunction(dll.getExportByName('LabRegisterSource'),'int',['pointer','uint','uint','uint','uint']);
const releaseShared=new NativeFunction(dll.getExportByName('LabReleaseShared'),'void',['pointer']);
const count=new NativeFunction(dll.getExportByName('LabReaderCount'),'uint',[]);
const stats=new NativeFunction(dll.getExportByName('LabStatistics'),'uint64',['uint','uint']);
const registerDownstream=new NativeFunction(dll.getExportByName('LabRegisterDownstream'),'int',['pointer','pointer']);
const unregisterDownstream=new NativeFunction(dll.getExportByName('LabUnregisterDownstream'),'void',['pointer']);
const downstreamCount=new NativeFunction(dll.getExportByName('LabDownstreamCount'),'uint',[]);
const downstreamStats=new NativeFunction(dll.getExportByName('LabDownstreamStatistics'),'uint64',['uint','uint']);
const downstreamEnergy=new NativeFunction(dll.getExportByName('LabDownstreamEnergy'),'double',['uint']);
const setEnabled=new NativeFunction(dll.getExportByName('LabSetEnabled'),'void',['int']);
let liveEnabled=settings.enabled!==false;
function receiveSettings(){recv('live_settings',message=>{liveEnabled=!!message.enabled;setEnabled(liveEnabled?1:0);send({type:'live_enabled',enabled:liveEnabled});receiveSettings();});}
setEnabled(settings.enabled===false?0:1);receiveSettings();
const originalCacheRead=Interceptor.replaceFast(base.add(0x67f850),dll.getExportByName('LabReadDownstream'));
new NativeFunction(dll.getExportByName('LabSetCacheReadOriginal'),'void',['pointer'])(originalCacheRead);
const factory=new NativeFunction(base.add(0x667d30),'void',['pointer','pointer','uint64','pointer','int64']);
const tracks=new Map(settings.tracks.map(t=>[t.carrier.toLowerCase(),t]));
const pending=new Set();const keep=[];
function receiveGenerationRetry(){recv('generation_retry_needed',message=>{
  pending.delete(message.source);
  send({type:'live_error',source:message.source,message:message.error||'GPU nicht verfügbar; Originalmix bleibt verfügbar. Nach Wiederherstellung den Track erneut laden.'});
  receiveGenerationRetry();
});}receiveGenerationRetry();
function stringObject(value){const data=Memory.allocUtf16String(value),object=Memory.alloc(32);object.writePointer(data);object.add(8).writeU64(0);object.add(16).writeU64(value.length);object.add(24).writeU64(Math.max(value.length,8));return {data,object};}
function pathOf(p){try{const n=p.add(16).readU64().toNumber(),cap=p.add(24).readU64().toNumber();return n>0&&n<4096?(cap>7?p.readPointer():p).readUtf16String(n):null;}catch{return null;}}
// Never acquire Traktor's URI lock from a getter: some callers already own it.
// Take a bounded read-only snapshot instead; abandon the lookup if its vector moves.
const getUris=new NativeFunction(base.add(0x4fe290),'pointer',[]);
let uriManager=ptr(0),writingCollection=false;
// Resolve the manager off the frequently called metadata getters. Some
// serialization callers already hold application locks when invoking them.
const uriTimer=setInterval(()=>{try{const value=getUris();if(!value.isNull()&&!value.equals(ptr(-1))){uriManager=value;clearInterval(uriTimer);}}catch{}},1000);
const collectionHandles=new Set();
Interceptor.attach(Process.getModuleByName('kernel32.dll').getExportByName('CreateFileW'),{
  onEnter(args){this.collection=false;try{const path=args[0].readUtf16String();this.collection=!!path&&/(^|[\\/])collection(?:new)?[.]nml$/i.test(path)&&(args[1].toUInt32()&0x40000000)!==0;if(this.collection)writingCollection=true;}catch{}},
  onLeave(handle){if(this.collection){if(!handle.equals(ptr(-1)))collectionHandles.add(handle.toString());else if(!collectionHandles.size)writingCollection=false;}}
});
Interceptor.attach(Process.getModuleByName('kernel32.dll').getExportByName('CloseHandle'),{
  onEnter(args){this.collection=collectionHandles.delete(args[0].toString());},
  onLeave(){if(this.collection&&!collectionHandles.size)writingCollection=false;}
});
const assignWide=new NativeFunction(base.add(0x519800),'pointer',['pointer','pointer','uint64']);
const dynamicBySource=new Map(),dynamicById=new Map(),failedSources=new Map();let requestId=0;
// Engine workers query associations after the synchronous load context has ended.
// Only nonzero IDs explicitly prepared in this session may resolve off-thread.
function associatedTrack(idPointer){
  if(writingCollection)return null;
  const context=trackContexts.get(Process.getCurrentThreadId());
  if(context)return context;
  if(!liveEnabled)return null;
  try{return dynamicById.get(idOf(idPointer))||null;}catch{return null;}
}
const trackContexts=new Map();
function idOf(p){return Array.from(new Uint8Array(p.readByteArray(256)),x=>x.toString(16).padStart(2,'0')).join('');}
function originalPath(object){
  if(!object.readPointer().equals(base.add(0x31d4c30)))return null;
  const manager=uriManager;if(manager.isNull())return null;
  const uriRegistry=manager.add(16).readPointer();
  if(uriRegistry.isNull())return null;
  const begin=uriRegistry.add(8).readPointer(),end=uriRegistry.add(16).readPointer(),id=object.add(16).readU32();
  if(id*48>=end.sub(begin).toUInt32())return null;
  const uri=begin.add(id*48),value=uri.add(32).readU8()===1?pathOf(uri):null;
  return begin.equals(uriRegistry.add(8).readPointer())&&end.equals(uriRegistry.add(16).readPointer())?value:null;
}
function ensureDynamic(object){
  if(!liveEnabled)return null;
  const source=originalPath(object);
  if(!source||!/[.](mp3|flac|wav|aif|aiff|m4a|mp4|ogg)$/i.test(source)||/[.]stem[.]mp4$/i.test(source))return null;
  const key=source.toLowerCase();let track=dynamicBySource.get(key);
  if(!track){
    const seconds=object.add(0x2dc).readFloat();
    if(Date.now()-(failedSources.get(key)||0)<10000)return null;
    const request_id=++requestId;let response=null;
    const operation=recv('dynamic_prepared:'+request_id,message=>{response=message;});
    send({type:'prepare_dynamic_track',source,seconds,request_id});operation.wait();
    if(!response?.track){failedSources.set(key,Date.now());send({type:'live_error',source,message:response?.error||'Automatische Vorbereitung fehlgeschlagen'});return null;}
    track=response.track;dynamicBySource.set(key,track);tracks.set(track.carrier.toLowerCase(),track);
    send({type:'dynamic_track_ready',source,carrier:track.carrier});
  }
  const id=idOf(object.add(0x530));
  // An unanalysed zero TrackID must never become a shared identity for unrelated files.
  if(/^0+$/.test(id))return track;
  const previous=dynamicById.get(id);
  if(previous&&previous.source.toLowerCase()!==key)return null;
  dynamicById.set(id,track);return track;
}
if(settings.dynamic_loading===true){
  // The normal load command returns a packed {allowed, deckType}. Preserve its
  // native permission/playing-deck checks; select the real Stem Deck only after
  // this source has an associated private carrier. This covers keyboard/mappings
  // and ordinary browser loads without relying on the context-menu command.
  Interceptor.attach(base.add(0xc405a0),{
    onEnter(args){this.track=null;this.previous=trackContexts.get(this.threadId);try{
      const source=originalPath(args[0]);
      this.track=ensureDynamic(args[0])||(source?dynamicBySource.get(source.toLowerCase()):null);
      if(this.track)trackContexts.set(this.threadId,this.track);
    }catch(error){send({type:'live_error',message:'Deck-Vorbereitung: '+error});}},
    onLeave(result){if(this.track&&(result.toUInt32()&255)){
      result.replace(result.and('0xffffffff').or(liveEnabled?'0x200000000':'0x0'));
      send({type:liveEnabled?'automatic_stem_load':'ordinary_track_load',source:this.track.source});
    }if(this.previous)trackContexts.set(this.threadId,this.previous);else trackContexts.delete(this.threadId);}
  });
  // Engine-side source resolution also needs the actual track context when a
  // filesystem entry has no fingerprint yet. Never redirect a global zero ID.
  Interceptor.attach(base.add(0x83cfe0),{
    onEnter(args){this.previous=trackContexts.get(this.threadId);try{
      const source=originalPath(args[2]);
      // A load already committed to a Stem Deck must resolve its carrier even
      // if the switch changes while the engine is opening its four readers.
      const track=!args[3].isNull()&&source?dynamicBySource.get(source.toLowerCase()):null;
      if(track)trackContexts.set(this.threadId,track);
    }catch(error){send({type:'live_error',message:'Deck-Quelle: '+error});}},
    onLeave(){if(this.previous)trackContexts.set(this.threadId,this.previous);else trackContexts.delete(this.threadId);}
  });
  // A file not yet imported can have an empty TrackID. Bind its metadata query
  // to the actual ContentEntryTrack on that thread, never to a global zero ID.
  for(const rva of [0x835360,0x854140])Interceptor.attach(base.add(rva),{
    onEnter(args){this.previous=trackContexts.get(this.threadId);try{
      const source=originalPath(args[1]);const track=source?dynamicBySource.get(source.toLowerCase()):null;
      if(track)trackContexts.set(this.threadId,track);
    }catch{}},
    onLeave(){if(this.previous)trackContexts.set(this.threadId,this.previous);else trackContexts.delete(this.threadId);}
  });
  for(const rva of [0x84ea30,0x84f7c0])Interceptor.attach(base.add(rva),{
    onEnter(args){this.eligible=false;this.disabledAssociation=false;try{
      if(writingCollection)return;
      // Rendering/scanning the browser must not probe every audio file. Defer
      // carrier creation to the actual deck-load decision above.
      const source=originalPath(args[0]);
      if(liveEnabled&&source&&/[.](mp3|flac|wav|aif|aiff|m4a|mp4|ogg)$/i.test(source)&&!/[.]stem[.]mp4$/i.test(source))
        this.eligible=dynamicBySource.has(source.toLowerCase());
      if(!liveEnabled){
        const source=originalPath(args[0]);this.disabledAssociation=!!source&&dynamicBySource.has(source.toLowerCase());
      }
      if(source&&trackContexts.get(this.threadId)?.source.toLowerCase()===source.toLowerCase())this.eligible=true;
    }catch(error){send({type:'live_error',message:'Automatisches Laden: '+error});}},
    onLeave(result){if(this.eligible)result.replace(ptr(1));else if(this.disabledAssociation)result.replace(ptr(0));}
  });
  // These are native TrackID -> associated-file queries, used by both the browser
  // and deck loader. Only identities prepared by this session are redirected.
  Interceptor.attach(base.add(0x86e6c0),{
    onEnter(args){this.track=associatedTrack(args[0]);},
    onLeave(result){if(this.track)result.replace(ptr(1));}
  });
  Interceptor.attach(base.add(0x86da20),{
    onEnter(args){this.out=args[0];this.track=associatedTrack(args[1]);},
    onLeave(){if(!this.track)return;try{
      const out=this.out;
      if(out.add(32).readU8()===0){out.writeByteArray(new Uint8Array(32));out.add(24).writeU64(7);}
      assignWide(out,Memory.allocUtf16String(this.track.carrier),uint64(this.track.carrier.length));out.add(32).writeU8(1);
    }catch(error){send({type:'live_error',message:'Stem-Pfad: '+error});}}
  });
}
function reference(track){
  new NativeFunction(Process.getModuleByName('ole32.dll').getExportByName('CoInitializeEx'),'int',['pointer','uint'])(ptr(0),0);
  const path=stringObject(track.source),out=Memory.alloc(16);out.writePointer(ptr(0));out.add(8).writePointer(ptr(0));
  factory(out,path.object,0,ptr(0),0);const object=out.readPointer();if(object.isNull())throw Error('Reference decoder unavailable');
  try{
    const table=object.readPointer(),rate=new NativeFunction(table.add(8).readPointer(),'uint64',['pointer'])(object).toNumber();
    const read=new NativeFunction(table.add(136).readPointer(),'int64',['pointer','pointer']);
    const seek=new NativeFunction(table.add(104).readPointer(),'void',['pointer','int64']);
    const left=Memory.alloc(4096),right=Memory.alloc(4096),planes=Memory.alloc(16);planes.writePointer(left);planes.add(8).writePointer(right);
    // Avoid silent intros: use a reference starting ten seconds into the track.
    const block=Math.floor(rate*10/1024);seek(object,block);
    const file=new File(track.reference_prefix+'-left.f32','wb');let samples=0;
    try{for(let i=0;i<64;i++){const n=read(object,planes).toNumber();if(n<=0)break;file.write(left.readByteArray(n*4));samples+=n;}}finally{file.close();}
    const metadata=new File(track.reference_prefix+'.json','w');metadata.write(JSON.stringify({source:track.source,rate,read_samples:samples,reference_start_frames:block*1024}));metadata.close();
    send({type:'generation_requested',source:track.source,cache:track.cache,reuse_cache:true,native_reference_prefix:track.reference_prefix});
  }finally{releaseShared(out.add(8).readPointer());}
}
Interceptor.attach(base.add(0x667d30),{
  onEnter(args){
    this.out=args[0];this.stem=args[2].toUInt32();const path=pathOf(args[1]);this.track=path?tracks.get(path.toLowerCase()):null;
    if(!this.track||this.stem>4)return;
    this.replacement=stringObject(this.track.source);args[1]=this.replacement.object;args[2]=ptr(0);args[3]=ptr(0);
  },
  onLeave(){
    if(!this.track||this.stem>4)return;
    try{
      const object=this.out.readPointer();if(object.isNull())throw Error('Factory returned null');
      const table=object.readPointer();if(!table.equals(base.add(0x31a1258)))throw Error('Unexpected audio interface '+table);
      const rate=new NativeFunction(table.add(8).readPointer(),'uint64',['pointer'])(object).toNumber();
      const frames=new NativeFunction(table.add(24).readPointer(),'uint64',['pointer'])(object).toNumber();
      const index=createCache(Memory.allocUtf16String(this.track.cache),uint64(Math.round(frames*44100/rate)),44100);
      if(index<0)throw Error('Cache creation '+index);
      const result=registerSource(object,this.stem,index,rate,1);if(result)throw Error('Reader registration '+result);
      send({type:'live_reader_registered',title:this.track.title,stem:this.stem,object:object.toString(),rate,frames,cache_index:index});
      if(!pending.has(this.track.source)){pending.add(this.track.source);const track=this.track;setImmediate(()=>{try{reference(track);}catch(e){send({type:'live_error',message:String(e),source:track.source});}});}
    }catch(e){send({type:'live_error',message:String(e)});}
  }
});
// Observe which downstream cache receives each source; never modifies or blocks reads.
Interceptor.attach(base.add(0x6871b0),{onEnter(args){this.cache=args[0];this.source=args[1].readPointer();},onLeave(){try{const result=registerDownstream(this.cache,this.source);if(result===0)send({type:'live_cache_registered',cache:this.cache.toString(),source:this.source.toString()});else if(result<0)send({type:'live_error',message:'Downstream registration '+result});}catch(e){send({type:'live_error',message:String(e)});}}});
Interceptor.attach(base.add(0x6798b0),{onEnter(args){unregisterDownstream(args[0]);}});
setInterval(()=>{const rows=[];for(let i=0;i<count();i++)rows.push({object:stats(i,0).toString(),stem:stats(i,1).toNumber(),calls:stats(i,2).toNumber(),replaced:stats(i,3).toNumber(),fallback:stats(i,4).toNumber()});const downstream=[];for(let i=0;i<downstreamCount();i++)downstream.push({object:downstreamStats(i,0).toString(),stem:downstreamStats(i,1).toNumber(),calls:downstreamStats(i,2).toNumber(),replaced:downstreamStats(i,3).toNumber(),frames:downstreamStats(i,4).toNumber(),position:downstreamStats(i,5).toNumber(),energy:downstreamEnergy(i)});send({type:'live_stats',rows,downstream});},2000);
send({type:'live_session_ready',tracks:settings.tracks.map(t=>t.title)});
