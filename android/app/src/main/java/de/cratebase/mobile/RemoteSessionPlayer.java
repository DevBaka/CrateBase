package de.cratebase.mobile;

import androidx.media3.common.*;
import java.util.concurrent.CopyOnWriteArrayList;

/** Keeps system transport buttons routed to the selected device rather than resuming local audio. */
@androidx.annotation.OptIn(markerClass=androidx.media3.common.util.UnstableApi.class)
public final class RemoteSessionPlayer extends ForwardingPlayer {
    private final PlaybackService service;
    private final CopyOnWriteArrayList<Player.Listener> listeners=new CopyOnWriteArrayList<>();
    public RemoteSessionPlayer(Player player,PlaybackService service){super(player);this.service=service;}
    @Override public void addListener(Player.Listener listener){listeners.add(listener);super.addListener(listener);}
    @Override public void removeListener(Player.Listener listener){listeners.remove(listener);super.removeListener(listener);}
    @Override public void play(){if(service.remoteMode)service.remoteCommand("play",null);else super.play();}
    @Override public void pause(){if(service.remoteMode)service.remoteCommand("pause",null);else super.pause();}
    @Override public void setPlayWhenReady(boolean value){if(service.remoteMode)service.remoteCommand(value?"play":"pause",null);else super.setPlayWhenReady(value);}
    @Override public void prepare(){if(!service.remoteMode)super.prepare();}
    @Override public void stop(){if(service.remoteMode)service.remoteCommand("pause",null);else super.stop();}
    @Override public void seekToNextMediaItem(){if(service.remoteMode)service.remoteCommand("next",null);else super.seekToNextMediaItem();}
    @Override public void seekToPreviousMediaItem(){if(service.remoteMode)service.remoteCommand("previous",null);else super.seekToPreviousMediaItem();}
    @Override public void seekToNext(){if(service.remoteMode)service.remoteCommand("next",null);else super.seekToNext();}
    @Override public void seekToPrevious(){if(service.remoteMode)service.remoteCommand("previous",null);else super.seekToPrevious();}
    @Override public void seekTo(long position){if(service.remoteMode)service.seekSelected(position);else super.seekTo(position);}
    @Override public void seekTo(int index,long position){if(service.remoteMode)service.seekSelected(position);else super.seekTo(index,position);}
    @Override public long getCurrentPosition(){return service.remoteMode?service.selectedPosition():super.getCurrentPosition();}
    @Override public long getDuration(){return service.remoteMode?service.selectedDuration():super.getDuration();}
    @Override public long getBufferedPosition(){return service.remoteMode?getCurrentPosition():super.getBufferedPosition();}
    @Override public long getContentPosition(){return service.remoteMode?getCurrentPosition():super.getContentPosition();}
    @Override public long getContentDuration(){return service.remoteMode?getDuration():super.getContentDuration();}
    @Override public long getContentBufferedPosition(){return service.remoteMode?getCurrentPosition():super.getContentBufferedPosition();}
    @Override public Timeline getCurrentTimeline(){
        Timeline original=super.getCurrentTimeline();if(!service.remoteMode)return original;
        return new androidx.media3.exoplayer.source.ForwardingTimeline(original){
            @Override public Timeline.Window getWindow(int index,Timeline.Window window,long projection){super.getWindow(index,window,projection);window.durationUs=service.remoteDurationAt(index)*1000;window.isSeekable=window.durationUs>0;window.defaultPositionUs=0;return window;}
            @Override public Timeline.Period getPeriod(int index,Timeline.Period period,boolean ids){super.getPeriod(index,period,ids);period.durationUs=service.remoteDurationAt(period.windowIndex)*1000;return period;}
        };
    }
    @Override public boolean isPlaying(){return service.remoteMode?service.selectedPlaying():super.isPlaying();}
    @Override public boolean getPlayWhenReady(){return service.remoteMode?service.selectedPlaying():super.getPlayWhenReady();}
    @Override public int getPlaybackState(){return service.remoteMode?(service.current==null?Player.STATE_IDLE:Player.STATE_READY):super.getPlaybackState();}
    @Override public Commands getAvailableCommands(){if(!service.remoteMode)return super.getAvailableCommands();return super.getAvailableCommands().buildUpon().addAll(COMMAND_PLAY_PAUSE,COMMAND_SEEK_IN_CURRENT_MEDIA_ITEM,COMMAND_SEEK_TO_NEXT_MEDIA_ITEM,COMMAND_SEEK_TO_PREVIOUS_MEDIA_ITEM,COMMAND_SEEK_TO_NEXT,COMMAND_SEEK_TO_PREVIOUS).build();}
    @Override public boolean isCommandAvailable(int command){return getAvailableCommands().contains(command);}
    public void changed(){
        Player.Events events=new Player.Events(new FlagSet.Builder().addAll(EVENT_TIMELINE_CHANGED,EVENT_MEDIA_METADATA_CHANGED,EVENT_PLAYBACK_STATE_CHANGED,EVENT_IS_PLAYING_CHANGED,EVENT_PLAY_WHEN_READY_CHANGED,EVENT_AVAILABLE_COMMANDS_CHANGED).build());
        for(Player.Listener listener:listeners){listener.onTimelineChanged(getCurrentTimeline(),TIMELINE_CHANGE_REASON_SOURCE_UPDATE);listener.onMediaMetadataChanged(getMediaMetadata());listener.onPlaybackStateChanged(getPlaybackState());listener.onIsPlayingChanged(isPlaying());listener.onAvailableCommandsChanged(getAvailableCommands());listener.onEvents(this,events);}
    }
}
