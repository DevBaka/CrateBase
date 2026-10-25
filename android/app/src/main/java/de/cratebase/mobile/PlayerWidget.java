package de.cratebase.mobile;

import android.app.PendingIntent;
import android.appwidget.*;
import android.content.*;
import android.graphics.Color;
import android.widget.RemoteViews;
import org.json.JSONObject;

public final class PlayerWidget extends AppWidgetProvider {
    static void update(Context context,JSONObject track,boolean playing,String status){
        android.content.SharedPreferences prefs=context.getSharedPreferences("widget",Context.MODE_PRIVATE);
        prefs.edit().putString("track",track==null?"":track.toString()).putBoolean("playing",playing).putString("status",status).apply();
        render(context);
    }
    private static void render(Context context){
        RemoteViews view=new RemoteViews(context.getPackageName(),R.layout.player_widget);
        android.content.SharedPreferences prefs=context.getSharedPreferences("widget",Context.MODE_PRIVATE);
        JSONObject track;try{track=new JSONObject(prefs.getString("track","{}"));}catch(Exception e){track=new JSONObject();}
        view.setTextViewText(R.id.widget_title,track.optString("displayTitle","In Cratebase Musik auswählen"));
        view.setTextViewText(R.id.widget_artist,track.optString("displayArtist",""));
        view.setTextViewText(R.id.widget_status,prefs.getString("status",""));
        view.setTextViewText(R.id.widget_play,prefs.getBoolean("playing",false)?"Ⅱ":"▶");
        int[] ids={R.id.widget_previous,R.id.widget_play,R.id.widget_next,R.id.widget_club,R.id.widget_extended,R.id.widget_intro};
        String[] actions={"previous","play","next","club","extended","intro"};
        for(int i=0;i<ids.length;i++)view.setOnClickPendingIntent(ids[i],PlaybackService.action(context,actions[i]));
        for(int i=3;i<ids.length;i++){boolean on=track.optBoolean(i==3?"clubReady":actions[i]);view.setTextColor(ids[i],on?Color.rgb(51,231,140):Color.WHITE);view.setTextViewText(ids[i],(on?"✓ ":"")+new String[]{"Club","Extended","Intro"}[i-3]);}
        view.setOnClickPendingIntent(R.id.widget_title,PendingIntent.getActivity(context,1,new Intent(context,MainActivity.class),PendingIntent.FLAG_IMMUTABLE));
        AppWidgetManager manager=AppWidgetManager.getInstance(context);manager.updateAppWidget(new ComponentName(context,PlayerWidget.class),view);
    }
    @Override public void onUpdate(Context context,AppWidgetManager manager,int[] ids){render(context);}
}
