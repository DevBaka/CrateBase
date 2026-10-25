package de.cratebase.mobile;

import java.net.URI;

/** Public HTTP requires a separate explicit opt-in; TLS failures never cause an automatic downgrade. */
public final class ConnectionPolicy {
    private ConnectionPolicy() {}
    public static String validate(String value, boolean allowLanHttp) {
        return validate(value,allowLanHttp,false);
    }
    public static String validate(String value, boolean allowLanHttp, boolean allowInternetHttp) {
        try {
            URI uri = new URI(value.trim());
            String host = uri.getHost();
            if (host == null || uri.getUserInfo() != null || uri.getRawQuery() != null || uri.getRawFragment() != null)
                throw new IllegalArgumentException("Bitte eine Server-Adresse ohne Schlüssel, Benutzer oder Query eingeben.");
            if(uri.getPort()!=-1&&(uri.getPort()<1||uri.getPort()>65535))throw new IllegalArgumentException("Port muss zwischen 1 und 65535 liegen.");
            if (!"https".equalsIgnoreCase(uri.getScheme()) && !("http".equalsIgnoreCase(uri.getScheme()) && (allowInternetHttp || allowLanHttp && privateHost(host))))
                throw new IllegalArgumentException("Für HTTP über eine Domain bitte ‚HTTP über Internet erlauben‘ aktivieren oder HTTPS verwenden.");
            return value.trim().replaceAll("/+$", "");
        } catch (java.net.URISyntaxException e) { throw new IllegalArgumentException("Ungültige Server-Adresse."); }
    }
    public static String withPort(String address,String port,boolean allowLanHttp){
        return withPort(address,port,allowLanHttp,false);
    }
    public static String withPort(String address,String port,boolean allowLanHttp,boolean allowInternetHttp){
        try{
            int number=Integer.parseInt(port.trim());
            if(number<1||number>65535)throw new IllegalArgumentException("Port muss zwischen 1 und 65535 liegen.");
            URI uri=new URI(validate(address,allowLanHttp,allowInternetHttp));
            return validate(new URI(uri.getScheme(),null,uri.getHost(),number,uri.getPath(),null,null).toString(),allowLanHttp,allowInternetHttp);
        }catch(NumberFormatException e){throw new IllegalArgumentException("Bitte einen gültigen Port eingeben, z. B. 5874.");}
        catch(java.net.URISyntaxException e){throw new IllegalArgumentException("Ungültige Server-Adresse.");}
    }
    private static boolean privateHost(String host) {
        if (host.equals("localhost") || host.equals("[::1]") || host.equals("::1")) return true;
        String[] parts = host.split("\\.");
        if (parts.length != 4) return false;
        int[] n = new int[4];
        try { for (int i=0;i<4;i++) { n[i]=Integer.parseInt(parts[i]); if(n[i]<0||n[i]>255)return false; } }
        catch (NumberFormatException e) { return false; }
        return n[0]==10 || n[0]==127 || (n[0]==192 && n[1]==168) || (n[0]==172 && n[1]>=16 && n[1]<=31);
    }
}
