// Configure a bounded analysis of the lab copy, excluding expensive speculative parameter inference.
// @category TraktorLab
import ghidra.app.script.GhidraScript;

public class LabAnalysis extends GhidraScript {
    @Override public void run() throws Exception {
        setAnalysisOption(currentProgram,"Decompiler Parameter ID","false");
        println("Lab copy: "+currentProgram.getName()+" / "+currentProgram.getExecutableSHA256());
    }
}
