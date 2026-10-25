package de.cratebase.mobile;

import org.json.*;
import java.util.*;

/** Device switching is explicit; connection loss never falls back to the phone speaker. */
public final class DesktopControl {
    private DesktopControl(){}
    public static JSONObject loadRequest(List<JSONObject> tracks,int selected,long position,boolean playing){
        if(tracks.isEmpty()||selected<0||selected>=tracks.size())throw new IllegalArgumentException("Kein Track zum Übernehmen ausgewählt.");
        int start=Math.min(Math.max(0,selected-25),Math.max(0,tracks.size()-250)),end=Math.min(tracks.size(),start+250);JSONArray ids=new JSONArray();for(int i=start;i<end;i++)ids.put(tracks.get(i).optLong("id"));
        return Api.json("ids",ids,"index",selected-start,"positionMs",Math.max(0,position),"playing",playing);
    }
}
