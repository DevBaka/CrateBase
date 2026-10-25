package de.cratebase.mobile;
import org.junit.Test;
import static org.junit.Assert.*;
public final class ConnectionPolicyTest {
    @Test public void externalRequiresHttps(){assertEquals("https://music.example.org",ConnectionPolicy.validate("https://music.example.org/",false));}
    @Test public void customHttpsPortsArePreserved(){assertEquals("https://music.example.org:5874",ConnectionPolicy.withPort("https://music.example.org","5874",false));assertEquals("https://music.example.org:8443/music",ConnectionPolicy.withPort("https://music.example.org:443/music","8443",false));}
    @Test public void rejectsInvalidPorts(){for(String port:new String[]{"0","65536","abc","-1"}){try{ConnectionPolicy.withPort("https://music.example.org",port,false);fail(port);}catch(IllegalArgumentException expected){}}}
    @Test public void lanRequiresOptIn(){assertEquals("http://192.168.1.5:5874",ConnectionPolicy.validate("http://192.168.1.5:5874",true));}
    @Test public void publicHttpRequiresSeparateOptIn(){
        assertEquals("http://music.example.org:5874",ConnectionPolicy.withPort("http://music.example.org","5874",false,true));
        assertEquals("http://8.8.8.8:5874",ConnectionPolicy.validate("http://8.8.8.8:5874",false,true));
        try{ConnectionPolicy.validate("http://music.example.org:5874",true,false);fail();}catch(IllegalArgumentException expected){}
    }
    @Test public void optInDoesNotDowngradeHttpsOrAllowOtherSchemes(){
        assertEquals("https://music.example.org:8443",ConnectionPolicy.validate("https://music.example.org:8443",false,true));
        for(String address:new String[]{"ftp://music.example.org","https://key@music.example.org","http://music.example.org?token=secret"}){try{ConnectionPolicy.validate(address,true,true);fail();}catch(IllegalArgumentException expected){}}
    }
    @Test public void rejectsUnsafeEndpoints(){for(String url:new String[]{"http://music.example.org","http://8.8.8.8","https://token@example.org","https://example.org?token=secret","file:///etc/passwd","http://192.168.999.1"}){try{ConnectionPolicy.validate(url,true);fail(url);}catch(IllegalArgumentException expected){}}}
    @Test public void rejectsLanUnlessEnabled(){try{ConnectionPolicy.validate("http://192.168.1.5",false);fail();}catch(IllegalArgumentException expected){}}
}
