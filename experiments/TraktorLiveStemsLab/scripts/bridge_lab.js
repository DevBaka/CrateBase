// Loaded only by launch_lab/attach_lab, which verify the private executable path.
// No JavaScript callbacks on the audio read path: native per-object vtable replacement.
const app=Process.getModuleByName('Traktor Pro 4.exe'),base=app.base;
const settings=BRIDGE_CONFIG;
const bridge=Module.load(settings.dll);
const configure=new NativeFunction(bridge.getExportByName('LabConfigure'),'int',['pointer']);
const register=new NativeFunction(bridge.getExportByName('LabRegister'),'int',['pointer','uint']);
const count=new NativeFunction(bridge.getExportByName('LabReaderCount'),'uint',[]);
const stats=new NativeFunction(bridge.getExportByName('LabStatistics'),'uint64',['uint','uint']);
const configured=configure(Memory.allocUtf16String(settings.cache));
if(configured!==0)throw new Error('Cache bridge configuration failed: '+configured);
function filePath(p){try{const size=p.add(16).readU64().toNumber(),capacity=p.add(24).readU64().toNumber();if(size>1024||size<1)return null;return(capacity>7?p.readPointer():p).readUtf16String(size);}catch{return null;}}
Interceptor.attach(base.add(0x667d30),{onEnter(args){this.output=args[0];this.stem=args[2].toUInt32();this.path=filePath(args[1]);this.selected=this.path&&this.path.toLowerCase()===settings.carrier.toLowerCase();},onLeave(){
    if(!this.selected||this.stem<1||this.stem>4)return;
    const object=this.output.readPointer();if(object.isNull())return;
    // Require exactly the observed MediaAudioStream vtable before touching an object.
    if(!object.readPointer().equals(base.add(0x31a1258)))return;
    send({type:'bridge_register',path:this.path,stem:this.stem,object:object.toString(),result:register(object,this.stem)});
}});
setInterval(()=>{const rows=[];for(let i=0;i<count();i++)rows.push({object:stats(i,0).toString(),stem:stats(i,1).toNumber(),calls:stats(i,2).toNumber(),replaced:stats(i,3).toNumber(),fallback:stats(i,4).toNumber()});send({type:'bridge_stats',rows});},2000);
send({type:'bridge_ready',cache:settings.cache,carrier:settings.carrier});
