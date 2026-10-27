// Audio API experiment inside the sandbox-owned process, independent of all UI controls.
// Does not load a deck, change playback, touch mappings, or interact with Windows prompts.
const settings=PROBE_CONFIG;
const app=Process.getModuleByName('Traktor Pro 4.exe'),base=app.base;
const bridge=Module.load(settings.dll);
const configure=new NativeFunction(bridge.getExportByName('LabConfigure'),'int',['pointer']);
const register=new NativeFunction(bridge.getExportByName('LabRegister'),'int',['pointer','uint']);
const configured=configure(Memory.allocUtf16String(settings.cache));if(configured&&!(configured===-1&&settings.reuseExistingMapping))throw new Error('configure '+configured);
const com=Process.getModuleByName('ole32.dll');
const coinit=new NativeFunction(com.getExportByName('CoInitializeEx'),'int',['pointer','uint']);
send({type:'probe_com',result:coinit(ptr(0),0)});
const factory=new NativeFunction(base.add(0x667d30),'void',['pointer','pointer','uint64','pointer','int64']);
const keep=[];
for(let stem=1;stem<=4;stem++){
    const path=Memory.allocUtf16String(settings.carrier),file=Memory.alloc(32),length=settings.carrier.length;
    file.writePointer(path);file.add(8).writeU64(0);file.add(16).writeU64(length);file.add(24).writeU64(length);
    const output=Memory.alloc(16);output.writePointer(ptr(0));output.add(8).writePointer(ptr(0));
    factory(output,file,settings.originalSource?0:stem,ptr(0),0);
    const object=output.readPointer();if(object.isNull())throw new Error('Native factory returned no stream, stem '+stem);
    const original=object.readPointer();if(!original.equals(base.add(0x31a1258)))throw new Error('Unexpected stream vtable '+original);
    let registered;
    if(settings.originalSource){
        const rate=new NativeFunction(original.add(8).readPointer(),'uint64',['pointer'])(object).toNumber();
        registered=new NativeFunction(bridge.getExportByName('LabRegisterSource'),'int',['pointer','uint','uint','uint','uint'])(object,stem,0,rate,1);
    }else registered=register(object,stem);
    if(registered)throw new Error('register '+registered);
    const table=object.readPointer(),read=new NativeFunction(table.add(17*8).readPointer(),'int64',['pointer','pointer']);
    const seek=new NativeFunction(table.add(13*8).readPointer(),'void',['pointer','int64']);
    const left=Memory.alloc(4096),right=Memory.alloc(4096),planes=Memory.alloc(16);planes.writePointer(left);planes.add(8).writePointer(right);
    for(const block of [0,1,172,1000,3000]){
        seek(object,block);const start=Date.now();const frames=read(object,planes).toNumber();const elapsed=Date.now()-start;
        const samples=[];for(let i=0;i<16;i++)samples.push(left.add(i*4).readFloat());
        send({type:'native_probe_block',stem,block,frames,elapsed_ms:elapsed,samples});
    }
    keep.push({object,output,file,path,left,right,planes});
}
// Objects intentionally live until this bounded private test process exits, avoiding
// unknown shared_ptr destruction ABI. These four streams never enter a deck.
globalThis.nativeProbeKeep=keep;
send({type:'native_audio_probe_complete',streams:keep.length});
