package de.cratebase.mobile;

import okhttp3.mockwebserver.*;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.RuntimeEnvironment;
import org.robolectric.annotation.Config;
import static org.junit.Assert.*;

@RunWith(RobolectricTestRunner.class)
@Config(sdk=35)
public final class ApiConnectionTest {
    private static final String KEY="temporary-connection-check-key-not-production";
    @Test public void verifiesActualCollectionBeforeSaving()throws Exception{
        try(MockWebServer server=new MockWebServer()){
            server.enqueue(new MockResponse().setBody("{\"name\":\"Cratebase\",\"version\":1}"));
            server.enqueue(new MockResponse().setBody("{\"tracks\":[{\"id\":1}],\"totals\":{\"count\":40073}}"));server.start();
            Api api=new Api(RuntimeEnvironment.getApplication());api.settings.edit().putString("url","https://previous.example:8443").commit();
            assertEquals(40073,api.checkConnection(server.url("").toString(),KEY,true));
            assertEquals("https://previous.example:8443",api.settings.getString("url",""));
            assertEquals("Bearer "+KEY,server.takeRequest().getHeader("Authorization"));assertEquals("/api/tracks/query",server.takeRequest().getPath());
        }
    }
    @Test public void failedLoginPreservesSavedConnection()throws Exception{
        try(MockWebServer server=new MockWebServer()){
            server.enqueue(new MockResponse().setResponseCode(401).setBody("{\"error\":\"Ungültiger Schlüssel\"}"));server.start();
            Api api=new Api(RuntimeEnvironment.getApplication());api.settings.edit().putString("url","https://previous.example:8443").commit();
            try{api.checkConnection(server.url("").toString(),KEY,true);fail();}catch(java.io.IOException expected){assertTrue(expected.getMessage().contains("Schlüssel"));}
            assertEquals("https://previous.example:8443",api.settings.getString("url",""));
        }
    }
    @Test public void rejectsWrongWebServer()throws Exception{
        try(MockWebServer server=new MockWebServer()){
            server.enqueue(new MockResponse().setBody("<html>Andere Webseite</html>"));server.start();
            try{new Api(RuntimeEnvironment.getApplication()).checkConnection(server.url("").toString(),KEY,true);fail();}
            catch(java.io.IOException expected){assertTrue(expected.getMessage().contains("Cratebase"));}
        }
    }
}
