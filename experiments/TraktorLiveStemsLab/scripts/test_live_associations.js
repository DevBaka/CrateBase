// Exercise association selection after the synchronous deck-load thread exits.
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const source=fs.readFileSync(require('node:path').join(__dirname,'live_session.js'),'utf8');
const begin=source.indexOf('function associatedTrack(');
const end=source.indexOf('\nconst trackContexts=',begin);
assert.ok(begin>=0&&end>begin,'association resolver is present');
function setup(){
 const scope={writingCollection:false,liveEnabled:true,trackContexts:new Map(),dynamicById:new Map(),Process:{getCurrentThreadId:()=>7},idOf:p=>{if(p===null)throw Error('unreadable ID');return p;}};
 vm.createContext(scope);vm.runInContext(source.slice(begin,end),scope);return scope;
}
test('engine worker resolves a prepared track without a synchronous load context',()=>{const s=setup(),track={source:'All Night Long.m4a'};s.dynamicById.set('known',track);assert.equal(s.associatedTrack('known'),track);assert.equal(s.associatedTrack('unknown'),null);assert.equal(s.associatedTrack('0'.repeat(512)),null);});
test('disabled extension does not create an off-thread association',()=>{const s=setup();s.dynamicById.set('known',{});s.liveEnabled=false;assert.equal(s.associatedTrack('known'),null);});
test('in-flight stem load retains its association when bypass is switched',()=>{const s=setup(),track={};s.trackContexts.set(7,track);s.liveEnabled=false;assert.equal(s.associatedTrack('known'),track);});
test('collection serialization never persists private associations',()=>{const s=setup();s.trackContexts.set(7,{});s.dynamicById.set('known',{});s.writingCollection=true;assert.equal(s.associatedTrack('known'),null);});
test('unreadable IDs fall back to the native association',()=>{const s=setup();assert.equal(s.associatedTrack(null),null);});
