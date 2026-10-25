package de.cratebase.mobile;

import android.app.*;
import android.content.*;
import android.os.*;
import androidx.annotation.Nullable;
import androidx.core.app.NotificationCompat;
import androidx.media3.common.*;
import androidx.media3.datasource.okhttp.OkHttpDataSource;
import androidx.media3.exoplayer.ExoPlayer;
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory;
import androidx.media3.session.*;
import org.json.*;
import java.util.*;
import java.util.concurrent.*;

@androidx.annotation.OptIn(markerClass=androidx.media3.common.util.UnstableApi.class)
public final class PlaybackService extends MediaSessionService {
    public static PlaybackService live;
    public static final String UPDATE="de.cratebase.mobile.UPDATE";
    public static final ExecutorService IO=Executors.newSingleThreadExecutor();
    private final Handler main=new Handler(Looper.getMainLooper());
    private MediaSession session;
    public ExoPlayer player;
    private Api api;
    public boolean remoteMode;
    public boolean switchingDevice;
    private JSONObject remoteState=new JSONObject();
    private boolean remoteConnected,remoteFetching;
    private long remoteUpdated;
    private RemoteSessionPlayer sessionPlayer;
    private Long pauseDesktopWhenReady;
    public JSONObject current;
    public String status="";
    private int errors;
    private JSONObject continuation;
    private boolean fetching;
    private int queueGeneration;
    private boolean restoring;
    private boolean destroyed;
    private final List<JSONObject> queue=new ArrayList<>();
    public static PendingIntent action(Context context,String name) {
        Intent intent=new Intent(context,PlaybackService.class).setAction(name);
        return PendingIntent.getService(context,name.hashCode(),intent,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
    }
    @Override public void onCreate() {
        super.onCreate();live=this;api=new Api(this);
        OkHttpDataSource.Factory data=new OkHttpDataSource.Factory(api.http).setDefaultRequestProperties(Collections.emptyMap());
        // Resolve a fresh key for each network request, including requests after a key rotation.
        androidx.media3.datasource.DataSource.Factory authorized=()->new androidx.media3.datasource.DefaultDataSource.Factory(this,()-> {
            try{return new OkHttpDataSource.Factory(api.http).setDefaultRequestProperties(Collections.singletonMap("Authorization","Bearer "+api.token())).createDataSource();}
            catch(Exception e){return data.createDataSource();}
        }).createDataSource();
        player=new ExoPlayer.Builder(this).setMediaSourceFactory(new DefaultMediaSourceFactory(authorized)).build();
        player.setAudioAttributes(new AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_MUSIC).build(),true);
        player.setHandleAudioBecomingNoisy(true);player.setWakeMode(C.WAKE_MODE_LOCAL);
        remoteMode=api.settings.getBoolean("desktopPlayback",false);
        sessionPlayer=new RemoteSessionPlayer(player,this);
        session=new MediaSession.Builder(this,sessionPlayer).setSessionActivity(PendingIntent.getActivity(this,0,new Intent(this,MainActivity.class),PendingIntent.FLAG_IMMUTABLE)).build();
        NotificationManager nm=getSystemService(NotificationManager.class);
        NotificationChannel tags=new NotificationChannel("tags","Track-Markierungen am Sperrbildschirm",NotificationManager.IMPORTANCE_LOW);
        tags.setLockscreenVisibility(Notification.VISIBILITY_PUBLIC);nm.createNotificationChannel(tags);
        player.addListener(new Player.Listener(){
            @Override public void onMediaItemTransition(@Nullable MediaItem item,int reason){
                if(remoteMode)return;
                current=null;for(JSONObject t:queue)if(item!=null&&item.mediaId.equals(t.optString("id"))){current=t;break;}
                publish();refreshCurrent();fetchMore();saveResume();
            }
            @Override public void onIsPlayingChanged(boolean playing){if(remoteMode)return;if(playing){errors=0;status="";}publish();}
            @Override public void onPlaybackStateChanged(int state){if(!remoteMode&&state==Player.STATE_READY&&pauseDesktopWhenReady!=null){long id=pauseDesktopWhenReady;pauseDesktopWhenReady=null;IO.execute(()->{try{api.call("POST","/api/player/command",Api.json("action","pause","expectedTrackId",id));}catch(Exception e){main.post(()->{status="Handy bereit; Desktop konnte nicht pausiert werden: "+e.getMessage();publish();});}});}}
            @Override public void onPlayerError(PlaybackException error){
                if(remoteMode)return;
                pauseDesktopWhenReady=null;
                if(!PlaybackErrors.isMissingSource(error)){
                    player.pause();status=PlaybackErrors.message(error);publish();return;
                }
                status="Stream nicht verfügbar. Nächster Track …";publish();
                if(++errors<queue.size()&&player.hasNextMediaItem()){player.seekToNextMediaItem();player.prepare();player.play();}
                else{status="Keine erreichbaren Tracks. Verbindung oder Server prüfen.";publish();}
            }
        });
        if(!remoteMode)restoreResume();else refreshRemote();
        main.post(poll);
    }
    private boolean refreshing;
    private final Runnable poll=new Runnable(){public void run(){if(remoteMode)refreshRemote();else if(current!=null){refreshCurrent();saveResume();}main.postDelayed(this,remoteMode?1500:5000);}};
    private void saveResume(){
        if(restoring||current==null||remoteMode)return;
        // Retain a bounded playback window rather than persisting the whole TB-sized collection.
        int selected=player.getCurrentMediaItemIndex();int start=Math.max(0,selected-20);int end=Math.min(queue.size(),selected+100);
        JSONArray rows=new JSONArray();for(int i=start;i<end;i++)rows.put(queue.get(i));
        getSharedPreferences("resume",MODE_PRIVATE).edit().putString("tracks",rows.toString()).putInt("selected",selected-start).putLong("position",player.getCurrentPosition()).apply();
    }
    private void restoreResume(){
        restoring=true;
        try{
            android.content.SharedPreferences prefs=getSharedPreferences("resume",MODE_PRIVATE);
            JSONArray rows=new JSONArray(prefs.getString("tracks","[]"));if(rows.length()==0)return;
            List<MediaItem> items=new ArrayList<>();for(int i=0;i<rows.length();i++){JSONObject track=rows.getJSONObject(i);queue.add(track);items.add(mediaItem(track,api.bitrate()));}
            int selected=Math.min(rows.length()-1,prefs.getInt("selected",0));player.setMediaItems(items,selected,prefs.getLong("position",0));
            current=queue.get(selected);status="Zum Fortsetzen Play drücken";publish();
        }catch(Exception ignored){queue.clear();}
        finally{restoring=false;}
    }
    private void refreshCurrent(){
        if(current==null||refreshing)return;refreshing=true;long id=current.optLong("id");
        IO.execute(()->{try{JSONObject latest=api.get("/api/tracks/"+id);main.post(()->{if(current!=null&&current.optLong("id")==id){current=latest;publish();}});}catch(Exception ignored){}finally{main.post(()->refreshing=false);}});
    }
    public void load(List<JSONObject> tracks,int selected,JSONObject sourceQuery){
        if(tracks.isEmpty())return;
        if(remoteMode){JSONObject request=DesktopControl.loadRequest(tracks,selected,0,true);remoteWork(()->{api.call("POST","/api/player/load",request);return api.get("/api/player");});return;}
        try{
            queueGeneration++;fetching=false;continuation=sourceQuery;
            queue.clear();queue.addAll(tracks);int bitrate=api.bitrate();List<MediaItem> items=new ArrayList<>();
            for(JSONObject track:tracks)items.add(mediaItem(track,bitrate));
            errors=0;player.setMediaItems(items,selected,0);player.prepare();player.play();status=bitrate+" kbit/s";publish();
        }catch(Exception e){status=e.getMessage();publish();}
    }
    private MediaItem mediaItem(JSONObject track,int bitrate){return new MediaItem.Builder().setMediaId(track.optString("id"))
        .setUri(api.base()+"/api/stream/"+track.optLong("id")+"?bitrate="+bitrate)
        .setMediaMetadata(new MediaMetadata.Builder().setTitle(track.optString("displayTitle")).setArtist(track.optString("displayArtist")).build()).build();}
    private void fetchMore(){
        if(continuation==null||fetching||player.getCurrentMediaItemIndex()<queue.size()-5)return;
        final JSONObject request;try{request=new JSONObject(continuation.toString());request.put("offset",queue.size());}catch(JSONException e){return;}
        fetching=true;int generation=queueGeneration;
        IO.execute(()->{try{
            JSONObject result=(JSONObject)api.call("POST","/api/tracks/query",request);JSONArray rows=result.getJSONArray("tracks");int total=result.getJSONObject("totals").getInt("count");
            main.post(()->{if(generation!=queueGeneration)return;fetching=false;
                List<MediaItem> additions=new ArrayList<>();for(int i=0;i<rows.length();i++){JSONObject track=rows.optJSONObject(i);boolean known=false;for(JSONObject t:queue)if(t.optLong("id")==track.optLong("id")){known=true;break;}if(!known){queue.add(track);additions.add(mediaItem(track,api.bitrate()));}}
                player.addMediaItems(additions);if(queue.size()>=total||rows.length()==0)continuation=null;
                if(player.getPlaybackState()==Player.STATE_ENDED&&!additions.isEmpty()){player.seekToNextMediaItem();player.prepare();player.play();}
            });
        }catch(Exception e){main.post(()->{if(generation==queueGeneration){fetching=false;status="Weitere Songs konnten nicht geladen werden: "+e.getMessage();publish();}});}});
    }
    public void toggleFlag(String field){
        if(current==null)return;long id=current.optLong("id");status="Markierung wird gespeichert …";publish();
        IO.execute(()->{try{
            JSONObject latest=api.get("/api/tracks/"+id);String key=field.equals("club")?"clubReady":field;
            api.call("PUT","/api/flags",Api.json("ids",new JSONArray().put(id),"field",field,"value",!latest.optBoolean(key)));
            JSONObject saved=api.get("/api/tracks/"+id);
            main.post(()->{if(current!=null&&current.optLong("id")==id)current=saved;status="Gespeichert";publish();});
        }catch(Exception e){main.post(()->{status="Nicht gespeichert: "+e.getMessage();publish();});}});
    }
    public void disconnect(){remoteMode=false;api.settings.edit().putBoolean("desktopPlayback",false).apply();pauseDesktopWhenReady=null;queueGeneration++;continuation=null;queue.clear();player.stop();player.clearMediaItems();current=null;status="Verbindung geändert";getSharedPreferences("resume",MODE_PRIVATE).edit().clear().apply();publish();}
    @Override public int onStartCommand(@Nullable Intent intent,int flags,int startId){
        int result=super.onStartCommand(intent,flags,startId);String action=intent==null?null:intent.getAction();
        if(remoteMode&&action!=null&&(action.equals("play")||action.equals("next")||action.equals("previous"))){remoteCommand(action.equals("play")?"toggle":action,null);return result;}
        if(action!=null)switch(action){
            case "play":if(player.isPlaying())player.pause();else if(player.getMediaItemCount()>0){if(player.getPlaybackState()==Player.STATE_IDLE||player.getPlayerError()!=null)player.prepare();player.play();}break;
            case "previous":player.seekToPreviousMediaItem();break;case "next":player.seekToNextMediaItem();break;
            case "club":case "extended":case "intro":case "trash":toggleFlag(action);break;
        }
        publish();return result;
    }
    public void publish(){
        if(destroyed)return;
        PlayerWidget.update(this,current,selectedPlaying(),(remoteMode?"Desktop · ":"")+status);
        sendBroadcast(new Intent(UPDATE).setPackage(getPackageName()));
        NotificationManager nm=getSystemService(NotificationManager.class);
        if(current==null){nm.cancel(42);return;}
        NotificationCompat.Builder b=new NotificationCompat.Builder(this,"tags").setSmallIcon(R.drawable.ic_cratebase)
            .setContentTitle(current.optString("displayTitle")).setContentText("Club · Extended · Intro  |  "+status)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC).setOnlyAlertOnce(true).setOngoing(true)
            .setContentIntent(PendingIntent.getActivity(this,0,new Intent(this,MainActivity.class),PendingIntent.FLAG_IMMUTABLE));
        for(String field:new String[]{"club","extended","intro"}){
            String name=field.substring(0,1).toUpperCase(Locale.GERMAN)+field.substring(1);
            if(current.optBoolean(field.equals("club")?"clubReady":field))name="✓ "+name;
            NotificationCompat.Action.Builder a=new NotificationCompat.Action.Builder(R.drawable.ic_cratebase,name,action(this,field));
            a.setAuthenticationRequired(false);b.addAction(a.build());
        }
        if(androidx.core.app.NotificationManagerCompat.from(this).areNotificationsEnabled())nm.notify(42,b.build());
    }
    @Override public MediaSession onGetSession(MediaSession.ControllerInfo controller){return session;}
    public boolean selectedPlaying(){return remoteMode?remoteConnected&&remoteState.optBoolean("playing"):player!=null&&player.isPlaying();}
    public long selectedDuration(){return remoteMode?remoteState.optLong("durationMs"):player.getDuration();}
    public long selectedPosition(){if(!remoteMode)return player.getCurrentPosition();return Math.min(Math.max(0,selectedDuration()),remoteState.optLong("positionMs")+(selectedPlaying()?SystemClock.elapsedRealtime()-remoteUpdated:0));}
    public void seekSelected(long position){if(remoteMode)remoteCommand("seek",Math.max(0,position));else player.seekTo(position);}
    public void setDesktopVolume(int value){remoteWork(()->{api.call("POST","/api/player/command",Api.json("action","volume","volume",value));return api.get("/api/player");});}
    public int desktopVolume(){return remoteState.optInt("volume",80);}
    public long remoteDurationAt(int index){if(index==remoteState.optInt("index"))return remoteState.optLong("durationMs");JSONArray tracks=remoteState.optJSONArray("queue");JSONObject track=tracks==null?null:tracks.optJSONObject(index);return track==null?0:track.optLong("durationMs");}
    private interface RemoteWork{JSONObject run()throws Exception;}
    private void remoteWork(RemoteWork work){IO.execute(()->{try{JSONObject state=work.run();main.post(()->{if(remoteMode&&!destroyed)applyRemote(state);});}catch(Exception e){main.post(()->{if(destroyed||!remoteMode)return;remoteConnected=false;status="Desktop nicht erreichbar: "+e.getMessage();sessionPlayer.changed();publish();});}});}
    public void remoteCommand(String action,Long position){remoteWork(()->{api.call("POST","/api/player/command",Api.json("action",action,"positionMs",position));return api.get("/api/player");});}
    private void refreshRemote(){if(remoteFetching)return;remoteFetching=true;IO.execute(()->{try{JSONObject state=api.get("/api/player");main.post(()->{if(remoteMode&&!destroyed)applyRemote(state);});}catch(Exception e){main.post(()->{if(remoteMode&&!destroyed){remoteConnected=false;status="Desktop nicht erreichbar: "+e.getMessage();sessionPlayer.changed();publish();}});}finally{main.post(()->remoteFetching=false);}});}
    private void applyRemote(JSONObject state){
        JSONArray rows=state.optJSONArray("queue");JSONObject active=state.optJSONObject("track");
        if(rows==null){rows=remoteState.optJSONArray("queue");if(rows==null)rows=new JSONArray();int found=-1;for(int i=0;i<rows.length();i++)if(active!=null&&rows.optJSONObject(i).optLong("id")==active.optLong("id")){found=i;break;}
            if(found<0&&active!=null){rows=new JSONArray().put(active);found=0;}
            try{state.put("queue",rows);state.put("index",Math.max(0,found));}catch(JSONException ignored){}
        }
        remoteState=state;remoteUpdated=SystemClock.elapsedRealtime();remoteConnected=true;status="Fernsteuerung · "+state.optString("name","Desktop");
        JSONObject track=state.optJSONObject("track");long before=current==null?-1:current.optLong("id");current=track;
        if(track!=null&&(before!=track.optLong("id")||player.getMediaItemCount()==0)){
            List<MediaItem> items=new ArrayList<>();JSONArray tracks=state.optJSONArray("queue");if(tracks!=null)for(int i=0;i<tracks.length();i++){JSONObject t=tracks.optJSONObject(i);items.add(new MediaItem.Builder().setMediaId(t.optString("id")).setUri("data:audio/wav;base64,"+android.util.Base64.encodeToString(remotePlaceholder(),android.util.Base64.NO_WRAP)).setMimeType("audio/wav").setMediaMetadata(new MediaMetadata.Builder().setTitle(t.optString("displayTitle")).setArtist(t.optString("displayArtist")).build()).build());}
            if(!items.isEmpty())player.setMediaItems(items,Math.max(0,Math.min(items.size()-1,state.optInt("index"))),0);player.pause();
        }
        if(track==null){player.clearMediaItems();player.pause();}
        sessionPlayer.changed();publish();
    }
    public void selectDesktop(boolean transfer){
        if(switchingDevice)return;
        if(remoteMode){refreshRemote();return;}
        final JSONObject request=transfer&&current!=null?DesktopControl.loadRequest(queue,player.getCurrentMediaItemIndex(),player.getCurrentPosition(),player.getPlayWhenReady()):null;
        switchingDevice=true;status="Desktop wird verbunden …";publish();
        IO.execute(()->{try{if(request!=null)api.call("POST","/api/player/load",request);JSONObject state=api.get("/api/player?includeQueue=true");main.post(()->{switchingDevice=false;if(destroyed)return;saveResume();remoteMode=true;player.pause();api.settings.edit().putBoolean("desktopPlayback",true).apply();applyRemote(state);});}
        catch(Exception e){main.post(()->{switchingDevice=false;status="Desktop-Umschaltung fehlgeschlagen: "+e.getMessage();publish();});}});
    }
    private static byte[] remotePlaceholder(){
        // MediaSession needs a valid timeline; this tiny local silent WAV never requests server audio.
        byte[] bytes=new byte[204];java.nio.ByteBuffer buffer=java.nio.ByteBuffer.wrap(bytes).order(java.nio.ByteOrder.LITTLE_ENDIAN);
        buffer.put(new byte[]{82,73,70,70}).putInt(196).put(new byte[]{87,65,86,69,102,109,116,32}).putInt(16).putShort((short)1).putShort((short)1).putInt(8000).putInt(16000).putShort((short)2).putShort((short)16).put(new byte[]{100,97,116,97}).putInt(160);return bytes;
    }
    public void selectPhone(){
        if(!remoteMode||switchingDevice)return;switchingDevice=true;status="Wiedergabe wird aufs Handy übernommen …";publish();
        IO.execute(()->{try{JSONObject state=api.get("/api/player?includeQueue=true");main.post(()->{
            switchingDevice=false;if(destroyed||!remoteMode)return;List<JSONObject> tracks=new ArrayList<>();JSONArray rows=state.optJSONArray("queue");if(rows!=null)for(int i=0;i<rows.length();i++)tracks.add(rows.optJSONObject(i));
            remoteMode=false;api.settings.edit().putBoolean("desktopPlayback",false).apply();remoteConnected=false;
            if(tracks.isEmpty()){player.clearMediaItems();current=null;status="Dieses Handy";sessionPlayer.changed();publish();return;}
            int index=Math.max(0,Math.min(tracks.size()-1,state.optInt("index")));pauseDesktopWhenReady=tracks.get(index).optLong("id");load(tracks,index,null);player.seekTo(state.optLong("positionMs"));if(!state.optBoolean("playing"))player.pause();sessionPlayer.changed();publish();
        });}catch(Exception e){main.post(()->{switchingDevice=false;remoteMode=false;api.settings.edit().putBoolean("desktopPlayback",false).apply();player.stop();player.clearMediaItems();queue.clear();current=null;continuation=null;queueGeneration++;restoreResume();status="Dieses Handy · Desktop nicht erreichbar; keine Übergabe. "+e.getMessage();sessionPlayer.changed();publish();});}});
    }
    @Override public void onDestroy(){saveResume();destroyed=true;main.removeCallbacksAndMessages(null);getSystemService(NotificationManager.class).cancel(42);PlayerWidget.update(this,current,false,"Zum Fortsetzen Play drücken");if(session!=null)session.release();if(player!=null)player.release();live=null;super.onDestroy();}
}
