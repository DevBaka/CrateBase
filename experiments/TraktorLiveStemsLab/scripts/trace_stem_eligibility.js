// Read-only diagnostics for the verified, isolated 4.5.1 process.
const base=Process.getModuleByName('Traktor Pro 4.exe').base;
const seen=new Set();
Interceptor.attach(base.add(0xa50260),{
  onEnter(args){
    try {
      const object=args[1],table=object.readPointer(),key=table.toString();
      const registry=new NativeFunction(base.add(0x4fe290),'pointer',[])().add(16).readPointer();
      const uri=registry.add(8).readPointer().add(object.add(16).readU32()*48);
      const n=uri.add(16).readU64().toNumber(),cap=uri.add(24).readU64().toNumber();
      send({type:'eligibility_source',seconds:object.add(0x2dc).readFloat(),source:n>0&&n<4096?(cap>7?uri.readPointer():uri).readUtf16String(n):null,tag:uri.add(32).readU8()});
      if(!seen.has(key)) {
        seen.add(key);
        send({type:'stem_eligibility_interface',table:table.sub(base).toString(),
          methods:Array.from({length:20},(_,i)=>table.add(i*8).readPointer().sub(base).toString())});
      }
      const fields=[];
      for(let offset=0x40;offset<0x640;offset+=8){
        try {
          const p=object.add(offset),n=p.add(16).readU64().toNumber(),capacity=p.add(24).readU64().toNumber();
          if(n>2&&n<2048&&capacity>=n&&capacity<4096){
            const value=(capacity>7?p.readPointer():p).readUtf16String(n);
            if(value&&/^[\x20-\uffff]+$/.test(value))fields.push({offset:offset.toString(16),value});
          }
        }catch{}
      }
      send({type:'stem_eligibility_fields',object:object.toString(),fields});
      const narrow=[];
      for(let offset=0x40;offset<0x640;offset+=8){try{
        const p=object.add(offset),n=p.add(16).readU64().toNumber(),capacity=p.add(24).readU64().toNumber();
        if(n>2&&n<2048&&capacity>=n&&capacity<4096){const value=(capacity>15?p.readPointer():p).readUtf8String(n);if(value&&!value.includes('\u0000'))narrow.push({offset:offset.toString(16),value});}
      }catch{}}
      send({type:'stem_eligibility_narrow',fields:narrow});
    }catch(error){send({type:'trace_error',message:String(error)});}
  }
});
send({type:'eligibility_trace_ready'});
