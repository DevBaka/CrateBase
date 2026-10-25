package de.cratebase.mobile;
import androidx.media3.datasource.HttpDataSource;
@androidx.annotation.OptIn(markerClass=androidx.media3.common.util.UnstableApi.class)
public final class PlaybackErrors {
    private PlaybackErrors(){}
    private static int responseCode(Throwable error){
        for(Throwable cause=error;cause!=null;cause=cause.getCause())if(cause instanceof HttpDataSource.InvalidResponseCodeException)return ((HttpDataSource.InvalidResponseCodeException)cause).responseCode;
        return -1;
    }
    public static boolean isMissingSource(Throwable error){int code=responseCode(error);return code==404||code==410;}
    public static String message(Throwable error){
        int code=responseCode(error);
        if(code==401||code==403)return "Stream-Zugriff verweigert. Zugriffsschlüssel in den Einstellungen prüfen.";
        if(code>=500)return "Server kann den Stream nicht erzeugen (HTTP "+code+"). Desktop-Server und Encoder prüfen. Mit Play erneut versuchen.";
        return "Wiedergabe fehlgeschlagen. Verbindung oder Audioformat prüfen. Mit Play erneut versuchen.";
    }
}
