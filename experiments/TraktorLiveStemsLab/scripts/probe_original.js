// Read-only native decoder comparison. Never loads a deck or changes collection entries.
const settings=ORIGINAL_PROBE_CONFIG;
const base=Process.getModuleByName('Traktor Pro 4.exe').base;
new NativeFunction(Process.getModuleByName('ole32.dll').getExportByName('CoInitializeEx'),'int',['pointer','uint'])(ptr(0),0);
const path=Memory.allocUtf16String(settings.source),file=Memory.alloc(32);file.writePointer(path);file.add(8).writeU64(0);file.add(16).writeU64(settings.source.length);file.add(24).writeU64(settings.source.length);
const output=Memory.alloc(16);output.writePointer(ptr(0));output.add(8).writePointer(ptr(0));
new NativeFunction(base.add(0x667d30),'void',['pointer','pointer','uint64','pointer','int64'])(output,file,0,ptr(0),0);
const object=output.readPointer();if(object.isNull())throw new Error('No native original decoder');
const table=object.readPointer(),rate=new NativeFunction(table.add(8).readPointer(),'uint64',['pointer'])(object).toNumber(),frames=new NativeFunction(table.add(24).readPointer(),'uint64',['pointer'])(object).toNumber();
const read=new NativeFunction(table.add(17*8).readPointer(),'int64',['pointer','pointer']);
const left=Memory.alloc(4096),right=Memory.alloc(4096),planes=Memory.alloc(16);planes.writePointer(left);planes.add(8).writePointer(right);
const leftFile=new File(settings.prefix+'-left.f32','wb'),rightFile=new File(settings.prefix+'-right.f32','wb');
let samples=0;const began=Date.now();for(let i=0;i<Math.ceil(rate*10/1024);i++){
 const count=read(object,planes).toNumber();if(count<=0)break;
 leftFile.write(left.readByteArray(count*4));rightFile.write(right.readByteArray(count*4));samples+=count;
}
leftFile.close();rightFile.close();
const result={source:settings.source,rate,total_frames:frames,read_samples:samples,read_ms:Date.now()-began};
const metadata=new File(settings.prefix+'.json','w');metadata.write(JSON.stringify(result));metadata.close();send({type:'original_probe_complete',...result});
globalThis.originalProbeKeep={object,output,file,path,left,right,planes};
