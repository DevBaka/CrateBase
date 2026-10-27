const base=Process.getModuleByName('Traktor Pro 4.exe').base;
for(const rva of [0x536230,0xc405a0])Interceptor.attach(base.add(rva),{
 onEnter(args){send({type:'deck_load_trace',rva:rva.toString(16),args:[args[0].toString(),args[1].toString(),args[2].toString(),args[3].toString()],stack:Thread.backtrace(this.context,Backtracer.ACCURATE).slice(0,8).map(p=>p.sub(base).toString())});}
});
