package de.cratebase.mobile;

import android.content.Context;
import android.content.SharedPreferences;
import android.net.ConnectivityManager;
import android.net.NetworkCapabilities;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import org.json.*;
import okhttp3.*;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.concurrent.TimeUnit;
import javax.crypto.*;
import javax.crypto.spec.GCMParameterSpec;

public final class Api {
    public static final java.util.concurrent.ExecutorService NETWORK=java.util.concurrent.Executors.newFixedThreadPool(3);
    public final SharedPreferences settings;
    private final Context context;
    public final OkHttpClient http = new OkHttpClient.Builder().connectTimeout(15,TimeUnit.SECONDS)
        .readTimeout(5,TimeUnit.MINUTES).followRedirects(false).followSslRedirects(false).build();
    public Api(Context context) { this.context=context.getApplicationContext();settings=context.getSharedPreferences("connection",Context.MODE_PRIVATE); }
    public String base() { return ConnectionPolicy.validate(settings.getString("url",""), settings.getBoolean("lan",false),settings.getBoolean("internetHttp",false)); }
    public boolean configured() { return !settings.getString("url","").isEmpty()&&!settings.getString("token","").isEmpty(); }
    public void configure(String url,String token,boolean allowLan) throws Exception {
        configure(url,token,allowLan,false);
    }
    public void configure(String url,String token,boolean allowLan,boolean allowInternetHttp) throws Exception {
        url=ConnectionPolicy.validate(url,allowLan,allowInternetHttp);
        if(token.trim().length()<32)throw new IllegalArgumentException("Zugriffsschlüssel ist zu kurz.");
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,key());
        String encrypted=Base64.encodeToString(cipher.doFinal(token.trim().getBytes(StandardCharsets.UTF_8)),Base64.NO_WRAP);
        settings.edit().putString("url",url).putString("token",encrypted).putString("iv",Base64.encodeToString(cipher.getIV(),Base64.NO_WRAP)).putBoolean("lan",allowLan).putBoolean("internetHttp",allowInternetHttp).apply();
    }
    private javax.crypto.SecretKey key() throws Exception {
        KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);
        if(store.containsAlias("cratebase-access"))return (javax.crypto.SecretKey)store.getKey("cratebase-access",null);
        KeyGenerator gen=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");
        gen.init(new KeyGenParameterSpec.Builder("cratebase-access",KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());return gen.generateKey();
    }
    public String token() throws Exception {
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE,key(),new GCMParameterSpec(128,Base64.decode(settings.getString("iv",""),Base64.NO_WRAP)));
        return new String(cipher.doFinal(Base64.decode(settings.getString("token",""),Base64.NO_WRAP)),StandardCharsets.UTF_8);
    }
    public Object call(String method,String path,JSONObject body) throws Exception {
        return requestTo(base(),token(),method,path,body);
    }
    /** Verify both the API and the collection before replacing a working saved connection. */
    public int checkConnection(String url,String key,boolean allowLan)throws Exception{
        return checkConnection(url,key,allowLan,false);
    }
    public int checkConnection(String url,String key,boolean allowLan,boolean allowInternetHttp)throws Exception{
        String base=ConnectionPolicy.validate(url,allowLan,allowInternetHttp);
        if(key.trim().length()<32)throw new IllegalArgumentException("Zugriffsschlüssel ist zu kurz.");
        Object response=requestTo(base,key.trim(),"GET","/api/status",null);
        if(!(response instanceof JSONObject)||!((JSONObject)response).optString("name").equals("Cratebase"))throw new IOException("An dieser Adresse antwortet keine Cratebase-API. Server-Port prüfen.");
        JSONObject page=(JSONObject)requestTo(base,key.trim(),"POST","/api/tracks/query",json("source",json("kind",0,"name","Collection"),"limit",1));
        if(page.optJSONObject("totals")==null||page.optJSONArray("tracks")==null)throw new IOException("Der Server liefert keine gültige Trackliste.");
        return page.getJSONObject("totals").getInt("count");
    }
    private Object requestTo(String address,String access,String method,String path,JSONObject body)throws Exception{
        Request.Builder request=new Request.Builder().url(address+path).header("Authorization","Bearer "+access);
        RequestBody data=body==null?null:RequestBody.create(body.toString(),MediaType.get("application/json; charset=utf-8"));
        if(method.equals("POST")||method.equals("PUT")||method.equals("PATCH"))request.method(method,data==null?RequestBody.create("",MediaType.get("application/json")):data);
        else request.method(method,null);
        try(Response response=http.newBuilder().readTimeout(30,TimeUnit.SECONDS).callTimeout(35,TimeUnit.SECONDS).build().newCall(request.build()).execute()) {
            String text=response.body()==null?"":response.body().string();
            if(!response.isSuccessful()) {
                String error="Server antwortet mit "+response.code();
                if(response.isRedirect())error="Der Server leitet diese Adresse um. Bitte die endgültige Server-Adresse mit dem richtigen HTTPS-Port eintragen.";
                try{error=new JSONObject(text).optString("error",error);}catch(JSONException ignored){}
                throw new IOException(error);
            }
            if(text.isEmpty())return new JSONObject();
            Object decoded;
            try{decoded=new JSONTokener(text).nextValue();}catch(JSONException e){throw new IOException("Keine gültige Cratebase-Antwort. Adresse, Port und Reverse Proxy prüfen.");}
            if(!(decoded instanceof JSONObject)&&!(decoded instanceof JSONArray))throw new IOException("An dieser Adresse antwortet keine Cratebase-API. Port prüfen.");
            return decoded;
        }
    }
    public JSONObject get(String path) throws Exception { return (JSONObject)call("GET",path,null); }
    public static JSONObject json(Object... pairs) {
        JSONObject result=new JSONObject();try{for(int i=0;i<pairs.length;i+=2)result.put((String)pairs[i],pairs[i+1]==null?JSONObject.NULL:pairs[i+1]);}catch(JSONException e){throw new IllegalArgumentException(e);}return result;
    }
    public int bitrate() {
        ConnectivityManager manager=(ConnectivityManager)context.getSystemService(Context.CONNECTIVITY_SERVICE);
        NetworkCapabilities net=manager.getNetworkCapabilities(manager.getActiveNetwork());
        boolean mobile=manager.isActiveNetworkMetered()||net==null||!net.hasTransport(NetworkCapabilities.TRANSPORT_WIFI);
        return settings.getInt(mobile?"mobileQuality":"wifiQuality",mobile?160:256);
    }
}
