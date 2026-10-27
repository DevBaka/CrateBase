const base=Process.getModuleByName('Traktor Pro 4.exe').base,seen=new Set();
for(const rva of [0x84ea30,0x84f7c0])Interceptor.attach(base.add(rva),{
  onEnter(args){
    const caller=this.returnAddress.sub(base).toString(),key=rva+':'+caller;
    if(seen.has(key))return;seen.add(key);
    send({type:'stem_decision_caller',method:rva.toString(16),caller,object:args[0].toString(),stack:Thread.backtrace(this.context,Backtracer.ACCURATE).slice(0,12).map(p=>p.sub(base).toString())});
  }
});
