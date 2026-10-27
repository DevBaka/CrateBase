const base=Process.getModuleByName('Traktor Pro 4.exe').base;
const registry=new NativeFunction(base.add(0x4fe3d0),'pointer',[])();
const sentinel=registry.readPointer(),pending=[sentinel.add(8).readPointer()],seen=new Set();
while(pending.length&&seen.size<100){const node=pending.pop();if(node.equals(sentinel)||seen.has(node.toString()))continue;seen.add(node.toString());try{
 if(node.add(25).readU8())continue;
 const provider=node.add(40).readPointer();if(!provider.isNull()){
  const table=provider.readPointer(),col=table.sub(8).readPointer(),type=base.add(col.add(12).readU32()).add(16).readUtf8String();
  send({type:'file_provider',key:node.add(36).readU32().toString(16),name:type,table:table.sub(base).toString(),methods:Array.from({length:9},(_,i)=>table.add(i*8).readPointer().sub(base).toString())});
 }
 pending.push(node.readPointer(),node.add(16).readPointer());
}catch(error){send({type:'provider_error',message:String(error)});}}
