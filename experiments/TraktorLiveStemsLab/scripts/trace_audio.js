// Observational tracing only: no return-value replacement and no audio modification.
const app=Process.getModuleByName('Traktor Pro 4.exe');
const base=app.base;
const seen=new Set();
const readers=[['MediaAudioStream.read',0x665360],['StemMixdownStream.read',0x665910],['WAVAudioStream.read',0x665ce0]];
for(const [name,rva] of readers){
    Interceptor.attach(base.add(rva),{onEnter(args){
        this.object=args[0];this.output=args[1];this.key=name+':'+this.object;
        if(!seen.has(this.key)){
            seen.add(this.key);let words=[];try{for(let i=0;i<10;i++)words.push(this.output.add(i*Process.pointerSize).readPointer().toString());}catch{}
            let state={};for(const offset of [0x8c,0x90,0x94,0xc4,0xc8,0xd4,0xd8,0xe0,0x100,0x108])try{state['0x'+offset.toString(16)]=this.object.add(offset).readU32();}catch{}
            send({type:'audio_first_read',name,object:this.object.toString(),output:this.output.toString(),words,state,thread:Process.getCurrentThreadId(),stack:Thread.backtrace(this.context,Backtracer.ACCURATE).slice(0,12).map(p=>p.sub(base).toString())});
        }
        this.report=(this.count=(globalThis.readCounts??=new Map()).get(this.key)||0)<4;
        globalThis.readCounts.set(this.key,this.count+1);
    },onLeave(result){if(this.report){let values=[];try{const pointer=this.output.readPointer();for(let i=0;i<8;i++)values.push(pointer.add(i*4).readFloat());}catch{}send({type:'audio_read_result',name,object:this.object.toString(),count:this.count,result:result.toString(),samples:values});}}});
}
send({type:'audio_trace_ready',base:base.toString()});
