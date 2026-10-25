package de.cratebase.mobile;
import org.json.*;
import java.util.*;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.annotation.Config;
import static org.junit.Assert.*;
@RunWith(RobolectricTestRunner.class) @Config(sdk=35)
public final class DesktopControlTest {
    @Test public void handoffKeepsIndexPositionAndPlayState(){List<JSONObject> tracks=new ArrayList<>();for(int i=0;i<500;i++)tracks.add(Api.json("id",i+1));JSONObject request=DesktopControl.loadRequest(tracks,300,45000,false);assertEquals(250,request.optJSONArray("ids").length());assertEquals(301,request.optJSONArray("ids").optLong(request.optInt("index")));assertEquals(45000,request.optLong("positionMs"));assertFalse(request.optBoolean("playing"));}
    @Test public void invalidSelectionsAreRejected(){try{DesktopControl.loadRequest(Collections.emptyList(),0,0,true);fail();}catch(IllegalArgumentException expected){}}
    @Test public void remoteSessionReportsDesktopInsteadOfLocalState()throws Exception{
        org.robolectric.android.controller.ServiceController<PlaybackService> owner=org.robolectric.Robolectric.buildService(PlaybackService.class).create();
        try{
            PlaybackService service=owner.get();service.remoteMode=true;
            java.lang.reflect.Method apply=PlaybackService.class.getDeclaredMethod("applyRemote",JSONObject.class);apply.setAccessible(true);
            JSONObject track=Api.json("id",4,"displayTitle","Desktop-Test","displayArtist","Cratebase");
            apply.invoke(service,Api.json("playing",true,"positionMs",45000,"durationMs",180000,"index",0,"track",track,"queue",new JSONArray().put(track)));
            java.lang.reflect.Field field=PlaybackService.class.getDeclaredField("sessionPlayer");field.setAccessible(true);RemoteSessionPlayer session=(RemoteSessionPlayer)field.get(service);
            assertTrue(session.isPlaying());assertFalse(service.player.isPlaying());assertEquals(180000,session.getDuration());assertTrue(session.getCurrentPosition()>=45000);assertEquals("Desktop-Test",session.getMediaMetadata().title.toString());assertEquals(180000,session.getCurrentTimeline().getWindow(0,new androidx.media3.common.Timeline.Window()).getDurationMs());
        }finally{owner.destroy();}
    }
}
