package de.cratebase.mobile;

import android.content.Context;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ListView;
import androidx.test.core.app.ActivityScenario;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import org.junit.*;
import org.junit.runner.RunWith;
import org.json.*;
import java.util.*;
import java.util.concurrent.atomic.*;
import static org.junit.Assert.*;

/** Run only against --mobile-device-fixture via adb reverse tcp:5875 tcp:5875. Never production. */
@RunWith(AndroidJUnit4.class)
public final class DeviceWorkflowTest {
    @Test public void encryptedLoginBrowseStreamTagsAndFavorites() throws Exception {
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();Api api=new Api(context);
        String key="cratebase-usb-test-only-not-a-real-library-key";
        api.configure("http://127.0.0.1:5875",key,true);
        assertEquals(key,api.token());assertFalse(api.settings.getString("token","").contains(key));
        assertEquals("Cratebase",api.get("/api/status").getString("name"));
        assertEquals(3,api.checkConnection("http://127.0.0.1:5875",key,true));
        JSONObject source=Api.json("kind",0,"name","Collection");
        JSONObject result=(JSONObject)api.call("POST","/api/tracks/query",Api.json("source",source,"limit",100));
        JSONArray rows=result.getJSONArray("tracks");assertEquals(3,rows.length());
        try(ActivityScenario<MainActivity> activity=ActivityScenario.launch(MainActivity.class)){
            waitFor(()->{AtomicBoolean ready=new AtomicBoolean();activity.onActivity(a->{ListView list=findList(a.findViewById(android.R.id.content));ready.set(list!=null&&list.getAdapter()!=null&&list.getAdapter().getCount()>=4);});return ready.get();});
            activity.onActivity(a->{ListView list=findList(a.findViewById(android.R.id.content));list.performItemClick(list.getChildAt(0),0,0);});
            waitFor(()->{AtomicBoolean ready=new AtomicBoolean();activity.onActivity(a->{ListView list=findList(a.findViewById(android.R.id.content));ready.set(list!=null&&list.getAdapter().getCount()==3);});return ready.get();});
            waitFor(()->PlaybackService.live!=null);
            activity.onActivity(a->{ListView list=findList(a.findViewById(android.R.id.content));list.performItemClick(list.getChildAt(0),0,0);});
            waitFor(()->{AtomicBoolean playing=new AtomicBoolean();InstrumentationRegistry.getInstrumentation().runOnMainSync(()->playing.set(PlaybackService.live!=null&&PlaybackService.live.player.isPlaying()));return playing.get();});
            AtomicLong trackId=new AtomicLong();InstrumentationRegistry.getInstrumentation().runOnMainSync(()->{PlaybackService.live.player.pause();trackId.set(PlaybackService.live.current.optLong("id"));});
            for(String field:new String[]{"club","extended","intro"}){
                InstrumentationRegistry.getInstrumentation().runOnMainSync(()->PlaybackService.live.toggleFlag(field));
                String jsonField=field.equals("club")?"clubReady":field;
                waitFor(()->api.get("/api/tracks/"+trackId.get()).optBoolean(jsonField));
            }
            api.call("POST","/api/favorites/USB-Test/0",Api.json("ids",new JSONArray().put(trackId.get())));
            JSONObject favorites=api.get("/api/favorites");assertEquals("USB-Test",favorites.getJSONArray("profiles").getJSONObject(0).getString("name"));
            api.call("PATCH","/api/tracks/"+trackId.get(),Api.json("values",Api.json("comment","Vom Android-Gerät geprüft")));
            assertEquals("Vom Android-Gerät geprüft",api.get("/api/tracks/"+trackId.get()).getString("comment"));
            // Widget binds six explicit, immutable service actions and renders real current flags.
            InstrumentationRegistry.getInstrumentation().runOnMainSync(()->{
                View widget=new android.widget.RemoteViews(context.getPackageName(),R.layout.player_widget).apply(context,null);
                for(int id:new int[]{R.id.widget_previous,R.id.widget_play,R.id.widget_next,R.id.widget_club,R.id.widget_extended,R.id.widget_intro})assertNotNull(widget.findViewById(id));
                PlaybackService.live.publish();
            });
            android.app.NotificationManager notifications=context.getSystemService(android.app.NotificationManager.class);
            waitFor(()->Arrays.stream(notifications.getActiveNotifications()).anyMatch(n->n.getId()==42&&n.getNotification().actions!=null&&n.getNotification().actions.length==3));
        }finally{
            InstrumentationRegistry.getInstrumentation().runOnMainSync(()->{if(PlaybackService.live!=null)PlaybackService.live.disconnect();});
            api.settings.edit().clear().commit();context.getSharedPreferences("resume",Context.MODE_PRIVATE).edit().clear().commit();
        }
    }
    private static ListView findList(View view){if(view instanceof ListView)return(ListView)view;if(view instanceof ViewGroup){ViewGroup g=(ViewGroup)view;for(int i=0;i<g.getChildCount();i++){ListView found=findList(g.getChildAt(i));if(found!=null)return found;}}return null;}
    private interface Condition{boolean get()throws Exception;}
    private static void waitFor(Condition condition)throws Exception{long end=android.os.SystemClock.elapsedRealtime()+20000;while(android.os.SystemClock.elapsedRealtime()<end){if(condition.get())return;Thread.sleep(100);}fail("Timeout waiting for device workflow");}
}
