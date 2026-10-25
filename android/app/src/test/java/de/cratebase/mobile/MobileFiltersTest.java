package de.cratebase.mobile;
import org.json.*;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.annotation.Config;
import static org.junit.Assert.*;
@RunWith(RobolectricTestRunner.class) @Config(sdk=35)
public final class MobileFiltersTest {
    @Test public void desktopFieldsAvailableWithoutCatalog(){JSONArray all=MobileFilters.catalog(new JSONArray());for(String key:new String[]{"bpm","club","extended","intro","trash","format","bitrate","genre","key","samplerate","missing"}){boolean found=false;for(int i=0;i<all.length();i++)if(all.optJSONObject(i).optString("key").equals(key))found=true;assertTrue(key,found);}}
    @Test public void rangesUseInclusiveDesktopComparisons(){JSONArray range=MobileFilters.create(Api.json("key","bpm","kind",1),10,"130","150");assertEquals(2,range.length());assertEquals(5,range.optJSONObject(0).optInt("operator"));assertEquals(7,range.optJSONObject(1).optInt("operator"));}
    @Test public void flagsAndNumbersDoNotOfferContains(){for(int kind:new int[]{1,2,3})for(int op:MobileFilters.operators(kind))assertTrue(op!=0&&op!=1);}
    @Test public void rejectsInvertedRangesAndInvalidNumbers(){for(String[] values:new String[][]{{"150","130"},{"abc","150"}}){try{MobileFilters.create(Api.json("key","bpm","kind",1),10,values[0],values[1]);fail();}catch(IllegalArgumentException expected){}}}
}
