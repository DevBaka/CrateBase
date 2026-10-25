package de.cratebase.mobile;

import android.content.Context;
import android.view.*;
import android.widget.ListView;
import androidx.test.core.app.ActivityScenario;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import org.junit.Test;
import org.junit.runner.RunWith;
import java.util.concurrent.atomic.AtomicBoolean;
import static org.junit.Assert.*;

/** Explicit opt-in, read-only library check. Preserves the access key and all library data. */
@RunWith(AndroidJUnit4.class)
public final class RealLibraryConnectionTest {
    @Test public void connectsAndRendersExistingCollection() throws Exception {
        String address=InstrumentationRegistry.getArguments().getString("serverUrl");
        org.junit.Assume.assumeTrue("Explicit serverUrl is required",address!=null);
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();Api api=new Api(context);
        String key=api.token();int count=api.checkConnection(address,key,true);assertTrue("Server collection must contain songs",count>0);
        String previous=api.settings.getString("url","");
        if(!previous.equals(address))api.settings.edit().putString("previousServerUrl",previous).commit();
        api.configure(address,key,true);
        try(ActivityScenario<MainActivity> activity=ActivityScenario.launch(MainActivity.class)){
            waitFor(()->{AtomicBoolean ready=new AtomicBoolean();activity.onActivity(a->{ListView list=find(a.findViewById(android.R.id.content));ready.set(list!=null&&list.getAdapter()!=null&&list.getAdapter().getCount()>3);});return ready.get();});
            activity.onActivity(a->{ListView list=find(a.findViewById(android.R.id.content));list.performItemClick(list.getChildAt(0),0,0);});
            waitFor(()->{AtomicBoolean ready=new AtomicBoolean();activity.onActivity(a->{ListView list=find(a.findViewById(android.R.id.content));if(list.getAdapter().getCount()<Math.min(count,100))return;try{Object row=list.getAdapter().getItem(0);java.lang.reflect.Field track=row.getClass().getDeclaredField("track");track.setAccessible(true);ready.set(track.get(row)!=null);}catch(ReflectiveOperationException ignored){}});return ready.get();});
            System.out.println("Verified real library on Android: "+count+" songs; collection page rendered.");
        }
    }
    private interface Condition{boolean get()throws Exception;}
    private static void waitFor(Condition condition)throws Exception{long end=android.os.SystemClock.elapsedRealtime()+30000;while(android.os.SystemClock.elapsedRealtime()<end){if(condition.get())return;Thread.sleep(100);}fail("Collection was not rendered on the phone");}
    private static ListView find(View view){if(view instanceof ListView)return(ListView)view;if(view instanceof ViewGroup){ViewGroup g=(ViewGroup)view;for(int i=0;i<g.getChildCount();i++){ListView found=find(g.getChildAt(i));if(found!=null)return found;}}return null;}
}
