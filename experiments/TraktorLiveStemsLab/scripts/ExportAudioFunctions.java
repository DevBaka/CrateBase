// Export only investigation-selected audio functions and their callers, never a whole reconstructed product.
// @category TraktorLab
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

public class ExportAudioFunctions extends GhidraScript {
    @Override public void run() throws Exception {
        String[] args=getScriptArgs();Path seeds=Paths.get(args[0]);Path output=Paths.get(args[1]);Files.createDirectories(output);
        LinkedHashSet<Function> functions=new LinkedHashSet<>();
        for(String line:Files.readAllLines(seeds,StandardCharsets.UTF_8)){
            line=line.trim();if(line.isEmpty()||line.startsWith("#"))continue;
            Address address=toAddr(line.replace("0x",""));Function function=getFunctionContaining(address);
            if(function==null){disassemble(address);function=createFunction(address,null);}
            if(function!=null)functions.add(function);
        }
        List<Function> direct=new ArrayList<>(functions);
        for(Function function:direct){
            if(function.getBody().getNumAddresses()<40)continue;
            int added=0;
            ReferenceIterator refs=currentProgram.getReferenceManager().getReferencesTo(function.getEntryPoint());
            while(refs.hasNext()){
                Reference ref=refs.next();if(!ref.getReferenceType().isCall())continue;
                Function caller=getFunctionContaining(ref.getFromAddress());if(caller!=null)functions.add(caller);if(++added>=20)break;
            }
        }
        DecompInterface decompiler=new DecompInterface();decompiler.openProgram(currentProgram);
        try(PrintWriter index=new PrintWriter(Files.newBufferedWriter(output.resolve("index.tsv"),StandardCharsets.UTF_8))){
            index.println("entry\tname\tbytes\tdecompiled\tcallers\tcallees");
            for(Function function:functions){
                monitor.checkCancelled();
                DecompileResults result=decompiler.decompileFunction(function,45,monitor);
                String code=result.decompileCompleted()?result.getDecompiledFunction().getC():"// Decompilation failed: "+result.getErrorMessage();
                StringBuilder text=new StringBuilder("// "+function.getEntryPoint()+" "+function.getName()+"\n");
                text.append("// Signature: ").append(function.getSignature()).append("\n");
                text.append("// Calling functions: ").append(function.getCallingFunctions(monitor)).append("\n");
                text.append("// Called functions: ").append(function.getCalledFunctions(monitor)).append("\n\n").append(code);
                Files.writeString(output.resolve(function.getEntryPoint().toString()+".c"),text,StandardCharsets.UTF_8);
                index.println(function.getEntryPoint()+"\t"+function.getName()+"\t"+function.getBody().getNumAddresses()+"\t"+result.decompileCompleted()+"\t"+function.getCallingFunctions(monitor).size()+"\t"+function.getCalledFunctions(monitor).size());index.flush();
                println("Exported "+function.getEntryPoint()+" "+function.getName());
            }
        }finally{decompiler.dispose();}
        println("Audio exports: "+functions.size());
    }
}
