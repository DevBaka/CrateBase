// Private Traktor 4.5.1: two visible rows of twelve native NGL controls.
// Keep the legacy object's twelve inline slots intact. A second owned bank is
// presented to legacy UI handlers only for the duration of a synchronous event.
// No audio callbacks or on-disk executable are modified by this extension.
'use strict';
const config=FAVORITES_CONFIG;
const base=Process.getModuleByName('Traktor Pro 4.exe').base;
const native=(rva,result,args)=>new NativeFunction(base.add(rva),result,args);
const allocate=native(0x2a2b9b0,'pointer',['uint64']);
const makeSwitch=native(0x1dd83e0,'pointer',['pointer']);
const makeFavorite=native(0xaa3ea0,'pointer',['pointer']);
const makeLabel=native(0x1dd6440,'pointer',['pointer']);
const addChild=native(0x1e0d330,'void',['pointer','pointer']);
const findChild=native(0x1e0fea0,'pointer',['pointer','int']);
const labelMode=native(0x1e022d0,'void',['pointer','int']);
const labelColor=native(0x1e01ae0,'void',['pointer','uint']);
const assign=native(0x519690,'pointer',['pointer','pointer','uint64']);
const destroyString=native(0x4ed800,'void',['pointer']);
const setText=native(0x1e04dc0,'void',['pointer','pointer']);
const updateSlot=native(0xa92e10,'void',['pointer','uint','pointer']);
const openPath=native(0xa914e0,'void',['pointer','pointer','char','char','char']);
const hitTest=native(0x1e164c0,'pointer',['pointer','pointer','char']);
const stopTimer=native(0xd1be10,'void',['pointer']);
const actionId=native(0xd1fe50,'int',['pointer']);
// These three instructions belong solely to the dynamically allocated MIDI
// parameter descriptors/name maps, not to the legacy inline favorite array.
// Verify the entire set before touching any instruction in the private process.
const midiPatches=[
 {rva:0xa5f92f,before:[0xc7,0x44,0x24,0x54,0x0b,0,0,0],after:[0xc7,0x44,0x24,0x54,0x17,0,0,0]},
 {rva:0xa5f9bb,before:[0xc7,0x44,0x24,0x6c,0x0b,0,0,0],after:[0xc7,0x44,0x24,0x6c,0x17,0,0,0]},
 {rva:0xa5fef0,before:[0x41,0x83,0xfc,0x0c],after:[0x41,0x83,0xfc,0x18]}
];
for(const patch of midiPatches){const bytes=new Uint8Array(base.add(patch.rva).readByteArray(patch.before.length));if(!patch.before.every((value,i)=>bytes[i]===value))throw Error('MIDI-Descriptor stimmt nicht mit Traktor 4.5.1 überein: '+patch.rva.toString(16));}
for(const patch of midiPatches)Memory.patchCode(base.add(patch.rva),patch.after.length,code=>code.writeByteArray(patch.after));
const legacyIds={primary:[31,32,33,34,35,36,37,38,39,40,41,42],
 icon:[1,4,6,8,10,12,14,16,18,20,23,25],shade:[2,3,5,7,9,11,13,15,17,19,22,24]};
const states=new Map(),keep=[],scopes=new Map();
const active=()=>scopes.get(Process.getCurrentThreadId());
function stringValue(p){const n=p.add(16).readU64().toNumber();if(n>65536)throw Error('Ungültiger Favoritenpfad');return (p.add(24).readU64().toNumber()>15?p.readPointer():p).readUtf8String(n);}
function stringObject(value){const p=Memory.alloc(32);p.writeByteArray(new Uint8Array(32));p.add(24).writeU64(15);const text=Memory.allocUtf8String(value);assign(p,text,unescape(encodeURIComponent(value)).length);return p;}
function geometry(p,x,y,w,h){const rect=Memory.alloc(16);rect.writeS32(x);rect.add(4).writeS32(y);rect.add(8).writeS32(x+w);rect.add(12).writeS32(y+h);new NativeFunction(p.readPointer().add(0x40).readPointer(),'void',['pointer','pointer'])(p,rect);}
function manager(form){return form.add(0x530).readPointer().add(0x650).readPointer();}
function save(state){send({type:'favorites_changed',slots:Array.from({length:12},(_,i)=>stringValue(state.bank.add(i*40)))});}
function bank(state,operation){
 const tid=Process.getCurrentThreadId(),previous=scopes.get(tid);
 if(previous){if(previous!==state)throw Error('Verschachtelte fremde Favoritenleiste');return operation();}
 const original=Memory.alloc(480),fields=Memory.alloc(16),array=state.form.add(0x2f0);
 Memory.copy(original,array,480);Memory.copy(fields,state.form.add(0x4d0),16);
 Memory.copy(array,state.bank,480);scopes.set(tid,state);
 for(const role of Object.keys(legacyIds))for(let i=0;i<12;i++)state.controls[role][i].add(0x58).writeS32(legacyIds[role][i]);
 state.form.add(0x4d0).writeS32(-1);state.form.add(0x4d4).writeS32(state.hover);
 try{return operation();}
 finally{
  state.hover=state.form.add(0x4d4).readS32();
  stopTimer(state.form.add(0x4e0));
  Memory.copy(state.bank,array,480);Memory.copy(array,original,480);Memory.copy(state.form.add(0x4d0),fields,16);
  for(const role of Object.keys(legacyIds))for(let i=0;i<12;i++)state.controls[role][i].add(0x58).writeS32(state.ids[role][i]);
  scopes.delete(tid);save(state);
 }
}
function layout(state){
 const width=state.form.add(0x30).readS32(),height=state.form.add(0x34).readS32(),row=Math.floor(height/2);
 if(width<120||row<8)return;
 for(let i=0;i<12;i++)for(let r=0;r<2;r++){
  const x=Math.floor(width*i/12),right=Math.floor(width*(i+1)/12),y=r*row;
  for(const role of ['shade','primary','icon']){
   const child=r?state.controls[role][i]:findChild(state.form,legacyIds[role][i]);
   if(child.isNull())continue;
   if(role==='icon')geometry(child,x+4,y+3,18,Math.max(1,row-3));else geometry(child,x,y,right-x,row);
  }
 }
}
function initialize(form){
 if(states.has(form.toString()))return;
 if(!form.readPointer().equals(base.add(0x31fd828)))throw Error('Unerwarteter Favoriten-Typ');
 const state={form,bank:Memory.alloc(480),controls:{primary:[],icon:[],shade:[]},ids:{primary:[],icon:[],shade:[]},hover:-1};
 state.bank.writeByteArray(new Uint8Array(480));keep.push(state);
 for(let i=0;i<12;i++){state.bank.add(i*40+24).writeU64(15);state.bank.add(i*40+32).writeU32(12);state.bank.add(i*40+36).writeU32(13);}
 const click=new NativeCallback((_listener,id,event,payload)=>{
  try{
   if(event!==0x186a1||payload.isNull())return;
   const i=state.ids.primary.indexOf(id);
   if(i<0||payload.add(8).readPointer().isNull())return;
   const path=stringValue(state.bank.add(i*40));if(!path)return;
   send({type:'favorites_open',slot:i+13});
   const text=stringObject(path);try{openPath(manager(form),text,1,0,0);}finally{destroyString(text);}
  }catch(error){send({type:'favorites_error',message:String(error)});}
 },'void',['pointer','int','int','pointer']);
 const table=Memory.alloc(Process.pointerSize);table.writePointer(click);const listener=Memory.alloc(Process.pointerSize);listener.writePointer(table);keep.push(click,table,listener);
 for(let i=0;i<12;i++)for(const role of ['shade','primary','icon']){
  // The legacy decoration IDs are Text/Label controls too: native updates
  // access fields +0xe8/+0xec. A ShadeAreaControl is smaller and incompatible.
  const child=role==='primary'?makeFavorite(allocate(0x1f8)):role==='icon'?makeSwitch(allocate(0x1d8)):makeLabel(allocate(0x180));
  if(child.isNull())throw Error('Favoriten-Bedienelement konnte nicht angelegt werden');
  // Primary favorites are SwitchControlEx (0x1f8), not plain labels. Use
  // their real constructor so text drawing, clicks and extra fields match.
  // NGL's text-style prefix contains the resource name/id and borrowed font
  // pointer, followed by mode/color. Its destructor does not own/free these
  // resources. Copy only that prefix, never the owning text/layout objects.
  const prototype=findChild(form,legacyIds[role][i]);
  Memory.copy(child.add(0xd0),prototype.add(0xd0),32);
  // TextLayout has its own font resource reference, distinct from the image
  // resource above. Copy its name/id only, not the owned shaping/text cache.
  Memory.copy(child.add(0xf8),prototype.add(0xf8),16);
  // Four POD text insets plus the eight rendering flags that enable those
  // insets and single-line clipping. Insets alone are ignored by a default
  // SwitchControlEx, producing text on top of the neighboring icon.
  Memory.copy(child.add(0x168),prototype.add(0x168),24);
  child.add(0x80).writeU32(prototype.add(0x80).readU32());
  const id=12000+i*3+['primary','icon','shade'].indexOf(role);state.controls[role].push(child);state.ids[role].push(id);child.add(0x58).writeS32(id);
  addChild(form,child);
  if(role==='shade'){labelColor(child,0xff2b2b2b);labelMode(child,0);}
  // Control::setListener is virtual +0xe8; forms have a different vtable.
  new NativeFunction(child.readPointer().add(0xe8).readPointer(),'void',['pointer','pointer'])(child,listener);
  if(role==='icon'){const label=stringObject(String(i+13));try{setText(child,label);}finally{destroyString(label);}}
 }
 states.set(form.toString(),state);
 bank(state,()=>{for(let i=0;i<12;i++){const value=(config.slots||[])[i]||'',text=stringObject(value);try{updateSlot(form,i,text);}finally{destroyString(text);}if(!value){const placeholder=stringObject((i+13)+' · Frei');try{setText(state.controls.primary[i],placeholder);}finally{destroyString(placeholder);}}}});
 layout(state);send({type:'favorites_ready',slots:24,rows:2,form:form.toString()});
}
// Scoped child lookup lets native drag/drop and label updates address the new
// controls without changing any legacy IDs or the first twelve saved targets.
Interceptor.attach(base.add(0x1e0fea0),{onEnter(args){this.state=active();this.form=args[0];this.id=args[1].toInt32();},onLeave(result){const s=this.state;if(!s||!this.form.equals(s.form))return;for(const role of Object.keys(legacyIds)){const i=legacyIds[role].indexOf(this.id);if(i>=0){result.replace(s.controls[role][i]);return;}}}});
Interceptor.attach(base.add(0xa8c140),{onEnter(args){this.form=args[0];},onLeave(){try{initialize(this.form);}catch(error){send({type:'favorites_error',message:String(error),stack:error.stack});}}});
Interceptor.attach(base.add(0xa83960),{onEnter(args){this.form=args[0];},onLeave(){const s=states.get(this.form.toString());if(s)try{layout(s);}catch(error){send({type:'favorites_error',message:String(error)});}}});
// Preserve native playlist/file drop processing. Only the target bank and its
// control IDs change; references and copy actions remain Traktor operations.
let dragOriginal;
const drag=new NativeCallback((form,event)=>{
 const s=states.get(form.toString());if(!s)return dragOriginal(form,event);
 let target=ptr(0);try{target=hitTest(form,event,1);}catch(_error){}
 const extra=Object.values(s.controls).some(list=>list.some(p=>p.equals(target)));
 s.dropBank=extra?1:0;
 if(extra)return bank(s,()=>dragOriginal(form,event));
 return dragOriginal(form,event);
},'void',['pointer','pointer']);
dragOriginal=new NativeFunction(Interceptor.replaceFast(base.add(0xa8d1e0),drag),'void',['pointer','pointer']);keep.push(drag);
let dropOriginal;
const drop=new NativeCallback((form,event)=>{const s=states.get(form.toString());if(s&&s.dropBank)return bank(s,()=>dropOriginal(form,event));return dropOriginal(form,event);},'void',['pointer','pointer']);
dropOriginal=new NativeFunction(Interceptor.replaceFast(base.add(0xa8ba80),drop),'void',['pointer','pointer']);keep.push(drop);
// The legacy Favorites tree is limited to twelve entries. Do not rebuild it
// from a temporary second bank; the visible second row has its own persistence.
let rebuildOriginal;
const rebuild=new NativeCallback((root,form)=>{if(active())return;rebuildOriginal(root,form);},'void',['pointer','pointer']);
rebuildOriginal=new NativeFunction(Interceptor.replaceFast(base.add(0xa8b1b0),rebuild),'void',['pointer','pointer']);keep.push(rebuild);
// The MIDI/keyboard properties are 0-based (12..23 = visible slots 13..24).
// Hand a local 0..11 value to the original action while exposing the owned
// second bank. Never pass an extended index to Traktor's twelve-slot array.
let mappingOriginal;
const mapping=new NativeCallback((listener,property,context,event)=>{
 const state=states.get(listener.sub(0x2b0).toString());
 if(!state||event.isNull())return mappingOriginal(listener,property,context,event);
 const value=event.add(4).readS32();
 if(event.readS32()!==1||value<12||value>=24)return mappingOriginal(listener,property,context,event);
 const action=actionId(property);
 if(action!==0xd80&&action!==0xd81)return mappingOriginal(listener,property,context,event);
 const local=Memory.alloc(12);Memory.copy(local,event,12);local.add(4).writeS32(value-12);
 send({type:'favorites_mapping',action:action===0xd81?'append':'select',slot:value+1});
 return bank(state,()=>mappingOriginal(listener,property,context,local));
},'void',['pointer','pointer','pointer','pointer']);
mappingOriginal=new NativeFunction(Interceptor.replaceFast(base.add(0xa80410),mapping),'void',['pointer','pointer','pointer','pointer']);keep.push(mapping);
send({type:'favorites_extension_loaded'});
