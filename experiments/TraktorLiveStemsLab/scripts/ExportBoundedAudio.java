// Private analysis only: PE unwind boundaries prevent accidentally starting mid-function.
// @category TraktorLab
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.SourceType;
import java.nio.file.*;
import java.util.*;
public class ExportBoundedAudio extends GhidraScript {
 public void run() throws Exception {
  String[] args=getScriptArgs();Path output=Paths.get(args[1]);Files.createDirectories(output);
  DecompInterface decompiler=new DecompInterface();decompiler.openProgram(currentProgram);
  try {
   for(String line:Files.readAllLines(Paths.get(args[0]))){
    String[] parts=line.trim().split("\\s+");if(parts.length<2||parts[0].startsWith("#"))continue;
    Address start=toAddr(parts[0]),end=toAddr(parts[1]).subtract(1);AddressSet body=new AddressSet(start,end);
    FunctionIterator iterator=currentProgram.getFunctionManager().getFunctions(body,true);List<Address> remove=new ArrayList<>();
    while(iterator.hasNext())remove.add(iterator.next().getEntryPoint());for(Address entry:remove)currentProgram.getFunctionManager().removeFunction(entry);
    disassemble(start);Function function=currentProgram.getFunctionManager().createFunction(null,start,body,SourceType.ANALYSIS);
    DecompileResults result=decompiler.decompileFunction(function,45,monitor);
    Files.writeString(output.resolve(start.toString()+".c"),"// PE unwind boundary "+start+".."+end+"\n"+(result.decompileCompleted()?result.getDecompiledFunction().getC():result.getErrorMessage()));
    println("Bounded audio export "+start);
   }
  }finally{decompiler.dispose();}
 }
}
