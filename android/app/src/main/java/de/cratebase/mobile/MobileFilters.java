package de.cratebase.mobile;

import org.json.*;

/** Field and operator IDs match the desktop contract. Keep labels usable before catalog loading completes. */
public final class MobileFilters {
    private MobileFilters(){}
    public static JSONArray catalog(JSONArray remote){
        JSONArray result=new JSONArray();
        String[][] defaults={{"club","Club-tauglich","3"},{"extended","Extended","3"},{"intro","Intro","3"},{"trash","Trash","3"},{"title","Titel","0"},{"artist","Artist","0"},{"album","Album","0"},{"albumartist","Album-Artist","0"},{"tracknumber","Track-Nr.","1"},{"bpm","BPM","1"},{"key","Key","0"},{"duration","Dauer (Sekunden)","1"},{"format","Format","0"},{"bitrate","Bitrate (kbps)","1"},{"samplerate","Samplerate (Hz)","1"},{"channels","Kanäle","1"},{"filesize","Dateigröße (MB)","1"},{"genre","Genre","0"},{"subgenre","Subgenre","0"},{"year","Jahr","1"},{"rating","Bewertung (0–5)","1"},{"playcount","Wiedergaben","1"},{"comment","Kommentar","0"},{"comment2","Kommentar 2","0"},{"color","Farbe","0"},{"lastplayed","Zuletzt gespielt","2"},{"added","Hinzugefügt","2"},{"modified","Metadaten geändert","2"},{"filemodified","Datei geändert","2"},{"availability","Dateistatus","0"},{"missing","Nicht erreichbar seit","2"},{"path","Dateipfad","0"}};
        for(String[] field:defaults){JSONObject match=null;for(int i=0;i<remote.length();i++){JSONObject candidate=remote.optJSONObject(i);if(candidate!=null&&candidate.optString("key").equals(field[0])){match=candidate;break;}}
            result.put(match==null?Api.json("key",field[0],"label",field[1],"kind",Integer.parseInt(field[2])):match);}
        for(int i=0;i<remote.length();i++){JSONObject field=remote.optJSONObject(i);if(field==null)continue;boolean known=false;for(int j=0;j<result.length();j++)if(result.optJSONObject(j).optString("key").equals(field.optString("key")))known=true;if(!known)result.put(field);}
        return result;
    }
    public static int[] operators(int kind){return kind==3?new int[]{2,3}:kind==0?new int[]{0,1,2,3,8,9}:kind==1?new int[]{2,3,4,5,6,7,10,8,9}:new int[]{2,3,4,5,6,7,8,9};}
    public static String operatorLabel(int op){return new String[]{"enthält","enthält nicht","ist gleich","ist ungleich","größer als / nach","mindestens / ab","kleiner als / vor","höchstens / bis","ist leer","ist nicht leer","zwischen (inklusive)"}[op];}
    public static JSONArray create(JSONObject field,int op,String value,String upper){
        int kind=field.optInt("kind");boolean allowed=false;for(int code:operators(kind))if(code==op)allowed=true;if(!allowed)throw new IllegalArgumentException("Dieser Vergleich passt nicht zum Feld.");
        String key=field.optString("key");value=value.trim();upper=upper.trim();
        if(op==8||op==9)return new JSONArray().put(Api.json("field",key,"operator",op,"value",""));
        if(value.isEmpty())throw new IllegalArgumentException("Bitte einen Filterwert eingeben.");
        if(kind==1){double low=number(value);if(op==10){double high=number(upper);if(low>high)throw new IllegalArgumentException("Der untere Wert muss kleiner oder gleich dem oberen Wert sein.");return new JSONArray().put(Api.json("field",key,"operator",5,"value",value)).put(Api.json("field",key,"operator",7,"value",upper));}}
        return new JSONArray().put(Api.json("field",key,"operator",op,"value",value));
    }
    private static double number(String value){try{double n=Double.parseDouble(value.replace(',','.'));if(Double.isFinite(n))return n;}catch(NumberFormatException ignored){}throw new IllegalArgumentException("Bitte eine gültige Zahl eingeben.");}
}
