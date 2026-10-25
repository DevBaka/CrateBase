package de.cratebase.mobile;

import android.appwidget.AppWidgetManager;
import android.content.Context;
import android.widget.RemoteViews;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.Robolectric;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.RuntimeEnvironment;
import org.robolectric.annotation.Config;
import static org.junit.Assert.*;

@RunWith(RobolectricTestRunner.class)
@Config(sdk=35)
public final class MobileUiTest {
    @Test public void setupScreenOpensWithoutAnAccount(){
        // Robolectric's default binder uses a null ComponentName; model an unavailable service instead.
        org.robolectric.Shadows.shadowOf(RuntimeEnvironment.getApplication()).declareComponentUnbindable(new android.content.ComponentName(RuntimeEnvironment.getApplication(),PlaybackService.class));
        try(org.robolectric.android.controller.ActivityController<MainActivity> activity=Robolectric.buildActivity(MainActivity.class).setup()){
            assertNotNull(activity.get().findViewById(android.R.id.content));
            android.app.AlertDialog dialog=org.robolectric.shadows.ShadowAlertDialog.getLatestAlertDialog();
            assertNotNull(dialog);assertTrue(dialog.isShowing());
        }
    }
    @Test public void backgroundPlayerCreatesMediaSession(){
        org.robolectric.android.controller.ServiceController<PlaybackService> service=Robolectric.buildService(PlaybackService.class).create();
        try{
            assertNotNull(service.get().player);assertNotNull(PlaybackService.live);
        }finally{service.destroy();}
    }
    @Test public void filtersOpenWithDesktopFieldsAndBpmDefault()throws Exception{
        org.robolectric.Shadows.shadowOf(RuntimeEnvironment.getApplication()).declareComponentUnbindable(new android.content.ComponentName(RuntimeEnvironment.getApplication(),PlaybackService.class));
        try(org.robolectric.android.controller.ActivityController<MainActivity> activity=Robolectric.buildActivity(MainActivity.class).setup()){
            org.robolectric.shadows.ShadowAlertDialog.getLatestAlertDialog().dismiss();
            java.lang.reflect.Method open=MainActivity.class.getDeclaredMethod("queryDialog");open.setAccessible(true);open.invoke(activity.get());
            org.robolectric.Shadows.shadowOf(android.os.Looper.getMainLooper()).idle();
            android.view.View dialog=org.robolectric.shadows.ShadowAlertDialog.getLatestAlertDialog().getWindow().getDecorView();
            for(String label:new String[]{"BPM","Genre","Club","Extended","Intro","Format","Bitrate","Key","140","+ Filter hinzufügen"})assertTrue(label,containsText(dialog,label));
        }
    }
    private static boolean containsText(android.view.View view,String value){if(view instanceof android.widget.TextView&&((android.widget.TextView)view).getText().toString().equals(value))return true;if(view instanceof android.view.ViewGroup){android.view.ViewGroup group=(android.view.ViewGroup)view;for(int i=0;i<group.getChildCount();i++)if(containsText(group.getChildAt(i),value))return true;}return false;}
    @Test public void freshInstallUsesMobileDataDefault() throws Exception {
        Context context=RuntimeEnvironment.getApplication();
        // Robolectric does not implement AndroidKeyStore; actual Keystore round-trip requires a device.
        assertFalse(new Api(context).configured());
        assertEquals(160,new Api(context).bitrate());
    }
    @Test public void largeWidgetContainsAllSixActions(){
        Context context=RuntimeEnvironment.getApplication();
        android.view.View view=new RemoteViews(context.getPackageName(),R.layout.player_widget).apply(context,null);
        for(int id:new int[]{R.id.widget_previous,R.id.widget_play,R.id.widget_next,R.id.widget_club,R.id.widget_extended,R.id.widget_intro})assertNotNull(view.findViewById(id));
    }
}
