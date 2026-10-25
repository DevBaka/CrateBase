package de.cratebase.mobile;
import androidx.media3.datasource.*;
import java.util.Collections;
import org.junit.Test;
import static org.junit.Assert.*;
@androidx.annotation.OptIn(markerClass=androidx.media3.common.util.UnstableApi.class)
@org.junit.runner.RunWith(org.robolectric.RobolectricTestRunner.class)
@org.robolectric.annotation.Config(sdk=35)
public final class PlaybackErrorsTest {
    private Throwable http(int code){return new RuntimeException(new HttpDataSource.InvalidResponseCodeException(code,"test",null,Collections.emptyMap(),new DataSpec(android.net.Uri.parse("http://localhost/stream")),new byte[0]));}
    @Test public void serverAndNetworkErrorsDoNotSkip(){assertFalse(PlaybackErrors.isMissingSource(http(503)));assertFalse(PlaybackErrors.isMissingSource(http(401)));assertFalse(PlaybackErrors.isMissingSource(new java.io.IOException("offline")));assertTrue(PlaybackErrors.message(http(503)).contains("503"));}
    @Test public void missingIndividualFilesCanBeSkipped(){assertTrue(PlaybackErrors.isMissingSource(http(404)));assertTrue(PlaybackErrors.isMissingSource(http(410)));}
}
