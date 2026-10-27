// Observe only the copied lab process. No audio or return-value modification.
const module=Process.getModuleByName('Traktor Pro 4.exe'),base=module.base;
function dump(p,n=128){try{return Array.from(new Uint8Array(p.readByteArray(n))).map(x=>x.toString(16).padStart(2,'0')).join('');}catch{return null;}}
function words(p,n=12){let x=[];for(let i=0;i<n;i++)try{x.push(p.add(i*8).readPointer().toString());}catch{x.push(null);}return x;}
Interceptor.attach(base.add(0x667d30),{onEnter(args){
 this.out=args[0];this.path=args[1];this.stack=this.context.rsp;
 send({type:'factory_enter',out:this.out.toString(),file:this.path.toString(),fileDump:dump(this.path),args:[args[2].toString(),args[3].toString()],stackArgs:words(this.stack.add(0x28),5),stack:Thread.backtrace(this.context,Backtracer.ACCURATE).slice(0,8).map(x=>x.sub(base).toString())});
},onLeave(){try{const object=this.out.readPointer();send({type:'factory_leave',object:object.toString(),dump:dump(object,240),inner:words(object.add(0x38))});}catch{}}});
const reported=new Set();
Interceptor.attach(base.add(0x665360),{onEnter(args){
 const object=args[0],key=object.toString();if(reported.has(key))return;reported.add(key);
 send({type:'reader_object',object:key,dump:dump(object,240),inner:words(object.add(0x38)),stack:Thread.backtrace(this.context,Backtracer.ACCURATE).slice(0,8).map(x=>x.sub(base).toString())});
}});
send({type:'factory_trace_ready'});
