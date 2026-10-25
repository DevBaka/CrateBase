package de.cratebase.mobile;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.graphics.Color;
import android.os.*;
import android.text.*;
import android.view.*;
import android.view.inputmethod.InputMethodManager;
import android.widget.*;
import androidx.media3.session.*;
import androidx.media3.session.MediaController;
import com.google.common.util.concurrent.ListenableFuture;
import org.json.*;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.util.*;

@androidx.annotation.OptIn(markerClass=androidx.media3.common.util.UnstableApi.class)
public final class MainActivity extends Activity {
    private static final int BG=Color.rgb(11,15,20),SURFACE=Color.rgb(21,28,36),GREEN=Color.rgb(51,231,140),TEXT=Color.rgb(236,240,246),MUTED=Color.rgb(158,172,190);
    private Api api;
    private final Handler main=new Handler(Looper.getMainLooper());
    private final ArrayDeque<Source> history=new ArrayDeque<>();
    private Source source=Source.home();
    private LinearLayout root,playerBox,favoritesBox;
    private TextView title,message,playerTitle;
    private EditText search;
    private ListView list;
    private final ArrayList<Entry> rows=new ArrayList<>();
    private final ArrayList<JSONObject> loaded=new ArrayList<>();
    private final LinkedHashSet<Long> selected=new LinkedHashSet<>();
    private BaseAdapter adapter;
    private JSONArray nodes=new JSONArray(),roots=new JSONArray(),fields=new JSONArray(),filters=new JSONArray();
    private JSONObject favoriteData=new JSONObject();
    private String sort="default",searchField="all",lastStatus="";
    private boolean descending,loading,favoritesExpanded;
    private int total,generation;
    private long lastRevision=-1;
    private ListenableFuture<MediaController> controllerFuture;
    private MediaController controller;
    private SeekBar seek;
    private TextView position;
    private boolean seeking;
    private Button playbackDevice;
    private final BroadcastReceiver updates=new BroadcastReceiver(){@Override public void onReceive(Context c,Intent i){renderPlayer();}};
    private static class Source {
        final String name;final int kind;final Long id;final String path;
        Source(String name,int kind,Long id,String path){this.name=name;this.kind=kind;this.id=id;this.path=path;}
        static Source home(){return new Source("Deine Bibliothek",-1,null,null);}
        JSONObject json(){return Api.json("kind",kind,"name",name,"playlistId",id,"path",path);}
    }
    private static class Entry {
        String title,sub;Source source;JSONObject track;boolean more;
        Entry(String title,String sub,Source source,JSONObject track){this.title=title;this.sub=sub;this.source=source;this.track=track;}
    }
    @Override public void onCreate(Bundle state){
        super.onCreate(state);api=new Api(this);
        try{filters=new JSONArray(api.settings.getString("metadataFilters","[]"));}catch(JSONException ignored){filters=new JSONArray();}
        sort=api.settings.getString("trackSort","default");searchField=api.settings.getString("trackSearchField","all");descending=api.settings.getBoolean("trackDescending",false);build();
        androidx.core.view.ViewCompat.setOnApplyWindowInsetsListener(root,(v,insets)->{
            androidx.core.graphics.Insets bars=insets.getInsets(androidx.core.view.WindowInsetsCompat.Type.systemBars()|androidx.core.view.WindowInsetsCompat.Type.ime());
            v.setPadding(dp(14),bars.top+dp(8),dp(14),bars.bottom+dp(8));return insets;
        });
        if(Build.VERSION.SDK_INT>=33&&checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=android.content.pm.PackageManager.PERMISSION_GRANTED)
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS},10);
        SessionToken token=new SessionToken(this,new ComponentName(this,PlaybackService.class));
        controllerFuture=new MediaController.Builder(this,token).buildAsync();
        controllerFuture.addListener(()->{try{controller=controllerFuture.get();renderPlayer();}catch(Exception e){show("Player konnte nicht starten: "+e.getMessage());}},androidx.core.content.ContextCompat.getMainExecutor(this));
        if(api.configured())reload(true);else settings();
    }
    private void build(){
        root=column();root.setBackgroundColor(BG);setContentView(root);
        LinearLayout header=row();TextView brand=label("CRATEBASE",23,GREEN);brand.setTypeface(null,android.graphics.Typeface.BOLD);header.addView(brand,new LinearLayout.LayoutParams(0,-2,1));
        header.addView(button("⚙",this::settings));root.addView(header);
        LinearLayout nav=row();nav.addView(button("‹ Zurück",this::goBack));nav.addView(button("Bibliothek",()->{history.clear();open(Source.home(),false);}));nav.addView(button("↻",()->reload(true)));nav.addView(button("+",this::createNode));root.addView(nav);
        title=label(source.name,20,TEXT);root.addView(title);
        LinearLayout searches=row();search=new EditText(this);search.setSingleLine(true);search.setTextColor(TEXT);search.setHintTextColor(MUTED);search.setHint("Collection durchsuchen …");search.setTextSize(14);
        searches.addView(search,new LinearLayout.LayoutParams(0,-2,1));searches.addView(button("Filter",this::queryDialog));root.addView(searches);
        search.addTextChangedListener(new TextWatcher(){public void beforeTextChanged(CharSequence s,int a,int c,int f){}public void onTextChanged(CharSequence s,int a,int b,int c){main.removeCallbacks(searchRequest);main.postDelayed(searchRequest,350);}public void afterTextChanged(Editable e){}});
        message=label("Verbinden …",12,MUTED);root.addView(message);
        list=new ListView(this);list.setDividerHeight(0);list.setCacheColorHint(BG);root.addView(list,new LinearLayout.LayoutParams(-1,0,1));
        adapter=new BaseAdapter(){
            public int getCount(){return rows.size();}public Object getItem(int i){return rows.get(i);}public long getItemId(int i){return i;}
            public View getView(int i,View old,android.view.ViewGroup parent){
                LinearLayout item;TextView a,b;Button more;
                if(old==null){item=row();item.setPadding(dp(8),dp(8),0,dp(8));LinearLayout labels=column();a=label("",16,TEXT);b=label("",12,MUTED);labels.addView(a);labels.addView(b);item.addView(labels,new LinearLayout.LayoutParams(0,-2,1));more=button("⋮",()->{});more.setFocusable(false);item.addView(more);item.setTag(new Object[]{a,b,more});}
                else item=(LinearLayout)old;
                Object[] views=(Object[])item.getTag();a=(TextView)views[0];b=(TextView)views[1];more=(Button)views[2];Entry e=rows.get(i);
                boolean chosen=e.track!=null&&selected.contains(e.track.optLong("id"));
                a.setText((chosen?"✓ ":e.source!=null?"▸  ":"")+e.title);b.setText(e.sub);item.setBackgroundColor(chosen?Color.rgb(20,65,47):BG);
                more.setVisibility(e.track!=null||e.source!=null&&e.source.id!=null&&e.source.kind!=4?View.VISIBLE:View.GONE);
                more.setOnClickListener(v->{if(e.track!=null)trackActions(e.track);else nodeActions(e.source);});return item;
            }
        };list.setAdapter(adapter);
        list.setOnItemClickListener((p,v,index,id)->{Entry e=rows.get(index);if(e.more){loadMore();return;}if(e.source!=null){open(e.source,true);return;}if(e.track!=null){if(!selected.isEmpty()){toggleSelection(e.track);return;}int n=loaded.indexOf(e.track);if(PlaybackService.live!=null)PlaybackService.live.load(loaded,n,request(source,search.getText().toString(),0));else show("Player startet noch. Bitte erneut antippen.");}});
        list.setOnItemLongClickListener((p,v,index,id)->{Entry e=rows.get(index);if(e.track!=null){toggleSelection(e.track);return true;}return false;});
        favoritesBox=column();Button favoriteToggle=button("Favoriten ▾ · aktuellen Song einsortieren",()->{favoritesExpanded=!favoritesExpanded;renderFavorites();});root.addView(favoriteToggle);root.addView(favoritesBox);
        playerBox=column();playerBox.setBackgroundColor(SURFACE);playerBox.setPadding(dp(8),dp(6),dp(8),dp(4));
        playbackDevice=button("Ausgabe: Dieses Handy ▾",this::deviceDialog);playerBox.addView(playbackDevice);
        playerTitle=label("Musik zum Vorhören auswählen",14,TEXT);playerBox.addView(playerTitle);
        LinearLayout transport=row();transport.addView(button("|◀",()->send("previous")),new LinearLayout.LayoutParams(0,dp(44),1));transport.addView(button("▶ / Ⅱ",()->send("play")),new LinearLayout.LayoutParams(0,dp(44),1));transport.addView(button("▶|",()->send("next")),new LinearLayout.LayoutParams(0,dp(44),1));playerBox.addView(transport);
        seek=new SeekBar(this);seek.setMax(1000);seek.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){public void onStartTrackingTouch(SeekBar s){seeking=true;}public void onStopTrackingTouch(SeekBar s){PlaybackService service=PlaybackService.live;if(service!=null&&service.selectedDuration()>0)service.seekSelected(service.selectedDuration()*s.getProgress()/1000);seeking=false;}public void onProgressChanged(SeekBar s,int v,boolean user){}});playerBox.addView(seek);
        position=label("",11,MUTED);playerBox.addView(position);LinearLayout flags=row();
        for(String field:new String[]{"club","extended","intro","trash"}){Button flag=button(cap(field),()->send(field));flag.setTag(field);flags.addView(flag,new LinearLayout.LayoutParams(0,dp(42),1));}playerBox.addView(flags);root.addView(playerBox);
    }
    private final Runnable searchRequest=()->{if(source.kind<0)open(new Source("Collection",0,null,null),true);else reload(false);};
    private void toggleSelection(JSONObject t){long id=t.optLong("id");if(!selected.add(id))selected.remove(id);adapter.notifyDataSetChanged();show(selected.isEmpty()?total+" Tracks":selected.size()+" ausgewählt · ⋮ für gemeinsame Aktionen");}
    private void open(Source next,boolean remember){if(remember)history.push(source);source=next;selected.clear();list.setSelection(0);reload(false);}
    private void goBack(){if(!selected.isEmpty()){selected.clear();adapter.notifyDataSetChanged();return;}if(!history.isEmpty())open(history.pop(),false);else open(Source.home(),false);}
    @Override public void onBackPressed(){goBack();}
    private void reload(boolean catalogs){
        final int request=++generation;loading=true;title.setText(source.name);show("Laden …");String term=search.getText().toString();Source currentSource=source;
        PlaybackService.IO.execute(()->{try{
            JSONArray nextNodes=catalogs?(JSONArray)api.call("GET","/api/playlists",null):nodes;
            JSONArray nextRoots=catalogs?(JSONArray)api.call("GET","/api/roots",null):roots;
            JSONArray nextFields=catalogs?(JSONArray)api.call("GET","/api/fields",null):fields;
            JSONObject nextFavorites=catalogs?api.get("/api/favorites"):favoriteData;
            ArrayList<Entry> entries=children(currentSource,nextNodes,nextRoots);
            ArrayList<JSONObject> tracks=new ArrayList<>();int count=0;
            if(currentSource.kind>=0){JSONObject result=(JSONObject)api.call("POST","/api/tracks/query",request(currentSource,term,0));JSONArray data=result.getJSONArray("tracks");count=result.getJSONObject("totals").getInt("count");for(int i=0;i<data.length();i++){JSONObject t=data.getJSONObject(i);tracks.add(t);entries.add(trackEntry(t));}}
            int finalCount=count;
            main.post(()->{if(request!=generation)return;nodes=nextNodes;roots=nextRoots;fields=nextFields;favoriteData=nextFavorites;loaded.clear();loaded.addAll(tracks);rows.clear();rows.addAll(entries);total=finalCount;appendMore();adapter.notifyDataSetChanged();loading=false;show(currentSource.kind<0?"Collection · Playlisten · Ordner":total+" Tracks"+(filters.length()>0?" · "+filters.length()+" Filter":""));renderFavorites();});
        }catch(Exception e){main.post(()->{if(request==generation){loading=false;show("Verbindung fehlgeschlagen: "+e.getMessage());}});}});
    }
    private JSONObject request(Source source,String term,int offset){return Api.json("source",source.json(),"search",term,"searchField",searchField,"sort",sort,"descending",descending,"limit",100,"offset",offset,"filters",filters);}
    private ArrayList<Entry> children(Source s,JSONArray nodeList,JSONArray rootList)throws Exception{
        ArrayList<Entry> result=new ArrayList<>();
        if(s.kind<0){
            result.add(new Entry("Collection","Alle Songs",new Source("Collection",0,null,null),null));
            result.add(new Entry("Club Collection","Club-tauglich markierte Songs",new Source("Club Collection",1,clubRoot(nodeList),null),null));
            result.add(new Entry("Datei nicht erreichbar","Später recherchieren",new Source("Datei nicht erreichbar",7,null,null),null));
            for(int i=0;i<rootList.length();i++){JSONObject r=rootList.getJSONObject(i);result.add(new Entry(r.optString("label"),"Festplattenordner",new Source(r.optString("label"),4,r.getLong("id"),""),null));}
        }
        if(s.kind<0||s.kind==3||s.kind==1){
            if(s.kind==1)result.add(new Entry("Nicht einsortiert","Noch in keiner Club-Playlist",new Source("Nicht einsortiert",6,null,null),null));
            Long parent=s.kind<0?null:s.id;Long club=clubRoot(nodeList);
            for(int i=0;i<nodeList.length();i++){JSONObject n=nodeList.getJSONObject(i);Long p=n.isNull("parentId")?null:n.getLong("parentId");long id=n.getLong("id");if(Objects.equals(parent,p)&&!(s.kind<0&&Objects.equals(club,id))){boolean folder=n.getInt("nodeType")==0;Source target=new Source(n.getString("name"),folder?3:2,id,null);result.add(new Entry(target.name,folder?"Playlist-Gruppe":"Playlist",target,null));}}
        }
        if(s.kind==4){JSONArray dirs=(JSONArray)api.call("GET","/api/folders?rootId="+s.id+"&path="+enc(s.path),null);for(int i=0;i<dirs.length();i++){JSONObject d=dirs.getJSONObject(i);Source target=new Source(d.getString("name"),4,s.id,d.getString("relativePath"));result.add(new Entry(target.name,"Ordner",target,null));}}
        return result;
    }
    private Long clubRoot(JSONArray list){for(int i=0;i<list.length();i++){JSONObject n=list.optJSONObject(i);if(n!=null&&n.optInt("nodeType")==0&&n.optString("name").equals("Club Collection")&&n.isNull("parentId"))return n.optLong("id");}return null;}
    private Entry trackEntry(JSONObject t){String flags=(t.optBoolean("clubReady")?"  ✓ Club":"")+(t.optBoolean("extended")?"  ✓ Extended":"")+(t.optBoolean("intro")?"  ✓ Intro":"")+(t.optBoolean("trash")?"  ✓ Trash":"");return new Entry(t.optString("displayTitle"),t.optString("displayArtist")+" · "+t.optString("genreRaw","")+" · "+t.optString("format").toUpperCase(Locale.ROOT)+" · "+time(t.optLong("durationMs"))+flags+(t.optString("fileAvailability").isEmpty()?"":" · Datei nicht erreichbar"),null,t);}
    private void appendMore(){if(loaded.size()<total){Entry e=new Entry("Weitere Songs laden",loaded.size()+" / "+total,null,null);e.more=true;rows.add(e);}}
    private void loadMore(){
        if(loading)return;loading=true;int request=generation;int offset=loaded.size();JSONObject body=request(source,search.getText().toString(),offset);
        PlaybackService.IO.execute(()->{try{JSONObject result=(JSONObject)api.call("POST","/api/tracks/query",body);JSONArray items=result.getJSONArray("tracks");main.post(()->{if(request!=generation)return;rows.removeIf(e->e.more);for(int i=0;i<items.length();i++){JSONObject t=items.optJSONObject(i);loaded.add(t);rows.add(trackEntry(t));}total=result.optJSONObject("totals").optInt("count");appendMore();adapter.notifyDataSetChanged();loading=false;});}catch(Exception e){main.post(()->{loading=false;show(e.getMessage());});}});
    }
    private void trackActions(JSONObject track){
        long[] ids=selected.isEmpty()?new long[]{track.optLong("id")}:selected.stream().mapToLong(Long::longValue).toArray();
        String[] choices={"Club setzen","Club entfernen","Extended setzen","Extended entfernen","Intro setzen","Intro entfernen","Trash setzen","Trash entfernen","Zu Playlist hinzufügen …","Metadaten bearbeiten …","Aus dieser Playlist entfernen"};
        new AlertDialog.Builder(this).setTitle(ids.length+" Track(s)").setItems(choices,(d,index)->{
            if(index<8){String field=new String[]{"club","extended","intro","trash"}[index/2];work(()->api.call("PUT","/api/flags",Api.json("ids",new JSONArray(ids),"field",field,"value",index%2==0)),true);}
            else if(index==8)pickPlaylist(ids);
            else if(index==9){if(ids.length!=1)show("Metadaten bitte einzeln bearbeiten.");else editMetadata(track);}
            else if(source.kind==2)new AlertDialog.Builder(this).setMessage("Aus dieser Playlist entfernen? Audiodateien bleiben erhalten.").setPositiveButton("Entfernen",(a,b)->work(()->{for(long id:ids)api.call("DELETE","/api/playlists/"+source.id+"/tracks/"+id,null);return null;},true)).setNegativeButton("Abbrechen",null).show();
            else show("Öffne dafür zuerst eine einzelne Playlist.");
        }).show();
    }
    private void pickPlaylist(long[] ids){
        ArrayList<JSONObject> playlists=new ArrayList<>();ArrayList<String> labels=new ArrayList<>();
        for(int i=0;i<nodes.length();i++){JSONObject n=nodes.optJSONObject(i);if(n.optInt("nodeType")==1){playlists.add(n);labels.add(nodePath(n));}}
        new AlertDialog.Builder(this).setTitle("In Playlist speichern").setItems(labels.toArray(new String[0]),(d,index)->work(()->api.call("POST","/api/playlists/"+playlists.get(index).optLong("id")+"/tracks",Api.json("ids",new JSONArray(ids))),true)).setNegativeButton("Abbrechen",null).show();
    }
    private String nodePath(JSONObject node){String name=node.optString("name");Long parent=node.isNull("parentId")?null:node.optLong("parentId");Set<Long> visited=new HashSet<>();while(parent!=null&&visited.add(parent)){JSONObject found=null;for(int i=0;i<nodes.length();i++){JSONObject n=nodes.optJSONObject(i);if(n.optLong("id")==parent){found=n;break;}}if(found==null)break;name=found.optString("name")+" / "+name;parent=found.isNull("parentId")?null:found.optLong("parentId");}return name;}
    private void editMetadata(JSONObject track){
        LinearLayout form=column();String[][] editable={{"title","Titel","title"},{"artist","Artist","artist"},{"album","Album","album"},{"albumartist","Album-Artist","albumArtist"},{"genre","Genre","genreRaw"},{"subgenre","Subgenre","subGenre"},{"year","Jahr","year"},{"bpm","BPM","bpm"},{"key","Key","musicalKey"},{"rating","Bewertung 0–5","rating"},{"comment","Kommentar","comment"},{"comment2","Kommentar 2","comment2"},{"color","Farbe","colorTag"},{"tracknumber","Track-Nr.","trackNumber"}};
        ArrayList<EditText> inputs=new ArrayList<>();for(String[] f:editable){form.addView(label(f[1],12,MUTED));EditText input=input(track.isNull(f[2])?"":track.optString(f[2]),false);form.addView(input);inputs.add(input);}
        ScrollView scroll=new ScrollView(this);scroll.addView(form);
        new AlertDialog.Builder(this).setTitle("Metadaten · "+track.optString("displayTitle")).setView(scroll).setPositiveButton("Speichern",(d,n)->{
            JSONObject values=new JSONObject();try{for(int i=0;i<editable.length;i++){String old=track.isNull(editable[i][2])?"":track.optString(editable[i][2]);String value=inputs.get(i).getText().toString();if(!value.equals(old))values.put(editable[i][0],value);}}catch(JSONException e){show(e.getMessage());return;}
            work(()->api.call("PATCH","/api/tracks/"+track.optLong("id"),Api.json("values",values)),true);
        }).setNegativeButton("Abbrechen",null).show();
    }
    private void createNode(){
        if(source.kind!=3&&source.kind!=1&&source.kind>=0){show("Öffne die Bibliothek oder eine Playlist-Gruppe, um eine Playlist anzulegen.");return;}
        LinearLayout form=column();EditText name=input("",false);name.setHint("Name");CheckBox folder=new CheckBox(this);folder.setText("Playlist-Gruppe");folder.setTextColor(TEXT);form.addView(name);form.addView(folder);Long parent=source.kind<0?null:source.id;
        new AlertDialog.Builder(this).setTitle("Playlist anlegen").setView(form).setPositiveButton("Anlegen",(d,n)->work(()->api.call("POST","/api/playlists",Api.json("name",name.getText().toString(),"parentId",parent,"folder",folder.isChecked())),true)).setNegativeButton("Abbrechen",null).show();
    }
    private void nodeActions(Source node){
        new AlertDialog.Builder(this).setTitle(node.name).setItems(new String[]{"Umbenennen","Verschieben …","Löschen …"},(d,i)->{
            if(i==0){EditText name=input(node.name,false);JSONObject existing=findNode(node.id);new AlertDialog.Builder(this).setTitle("Umbenennen").setView(name).setPositiveButton("Speichern",(a,b)->work(()->api.call("PUT","/api/playlists/"+node.id,Api.json("name",name.getText().toString(),"parentId",existing.isNull("parentId")?null:existing.optLong("parentId"))),true)).setNegativeButton("Abbrechen",null).show();}
            else if(i==1){ArrayList<Long> ids=new ArrayList<>();ArrayList<String> names=new ArrayList<>();ids.add(null);names.add("Bibliothek (Hauptebene)");for(int j=0;j<nodes.length();j++){JSONObject n=nodes.optJSONObject(j);if(n.optInt("nodeType")==0&&n.optLong("id")!=node.id){ids.add(n.optLong("id"));names.add(nodePath(n));}}new AlertDialog.Builder(this).setTitle("Zielgruppe").setItems(names.toArray(new String[0]),(a,b)->work(()->api.call("PUT","/api/playlists/"+node.id,Api.json("name",node.name,"parentId",ids.get(b))),true)).show();}
            else new AlertDialog.Builder(this).setMessage("Playlist/Gruppe samt Untergruppen löschen? Audiodateien bleiben erhalten.").setPositiveButton("Löschen",(a,b)->work(()->api.call("DELETE","/api/playlists/"+node.id,null),true)).setNegativeButton("Abbrechen",null).show();
        }).show();
    }
    private JSONObject findNode(long id){for(int i=0;i<nodes.length();i++)if(nodes.optJSONObject(i).optLong("id")==id)return nodes.optJSONObject(i);return new JSONObject();}
    private void queryDialog(){
        fields=MobileFilters.catalog(fields);LinearLayout form=column();form.setPadding(dp(16),dp(8),dp(16),0);ArrayList<String> names=new ArrayList<>();names.add("Standard-Reihenfolge");for(int i=0;i<fields.length();i++)names.add(fields.optJSONObject(i).optString("label"));
        Spinner sorting=spinner(names.toArray(new String[0]));int selectedSort=0;for(int i=0;i<fields.length();i++)if(fields.optJSONObject(i).optString("key").equals(sort))selectedSort=i+1;sorting.setSelection(selectedSort);form.addView(label("Sortieren nach",12,MUTED));form.addView(sorting);
        CheckBox desc=new CheckBox(this);desc.setText("Absteigend");desc.setTextColor(TEXT);desc.setChecked(descending);form.addView(desc);
        Spinner searching=spinner(names.toArray(new String[0]));int selectedSearch=0;for(int i=0;i<fields.length();i++)if(fields.optJSONObject(i).optString("key").equals(searchField))selectedSearch=i+1;searching.setSelection(selectedSearch);form.addView(label("Suchfeld · Standard = alle Metadaten",12,MUTED));form.addView(searching);
        final JSONArray[] draft={new JSONArray()};for(int i=0;i<filters.length();i++)draft[0].put(filters.opt(i));
        LinearLayout active=column();form.addView(active);
        Runnable[] redraw={null};redraw[0]=()->{active.removeAllViews();active.addView(label("Aktive Filter: "+draft[0].length()+" · alle müssen zutreffen",12,MUTED));for(int i=0;i<draft[0].length();i++){final int index=i;JSONObject f=draft[0].optJSONObject(i);String name=f.optString("field");for(int j=0;j<fields.length();j++)if(fields.optJSONObject(j).optString("key").equals(name)){name=fields.optJSONObject(j).optString("label");break;}active.addView(button(name+" "+MobileFilters.operatorLabel(f.optInt("operator"))+" "+f.optString("value")+"  ×",()->{JSONArray next=new JSONArray();for(int j=0;j<draft[0].length();j++)if(j!=index)next.put(draft[0].opt(j));draft[0]=next;redraw[0].run();}));}};redraw[0].run();
        form.addView(label("Filter hinzufügen · Schnellauswahl",14,TEXT));
        Spinner field=spinner(names.subList(1,names.size()).toArray(new String[0]));
        LinearLayout quick=row();HorizontalScrollView quickScroll=new HorizontalScrollView(this);quickScroll.addView(quick);form.addView(quickScroll);
        for(String[] preset:new String[][]{{"bpm","BPM"},{"genre","Genre"},{"club","Club"},{"extended","Extended"},{"intro","Intro"},{"trash","Trash"},{"format","Format"},{"bitrate","Bitrate"},{"key","Key"}})quick.addView(button(preset[1],()->{for(int i=0;i<fields.length();i++)if(fields.optJSONObject(i).optString("key").equals(preset[0])){field.setSelection(i);break;}}));
        form.addView(label("Metadatenfeld · alle Felder wie am Desktop",12,MUTED));form.addView(field);
        Spinner op=spinner(new String[]{"ist gleich"});form.addView(op);final int[][] codes={new int[]{2}};
        AutoCompleteTextView value=new AutoCompleteTextView(this);value.setTextColor(TEXT);value.setHintTextColor(MUTED);value.setTextSize(14);value.setSingleLine(true);value.setThreshold(0);form.addView(value);
        Spinner yesNo=spinner(new String[]{"Ja · markiert","Nein · nicht markiert"});form.addView(yesNo);
        EditText upper=input("150",false);upper.setHint("Oberer Wert (inklusive)");upper.setSingleLine(true);upper.setInputType(android.text.InputType.TYPE_CLASS_NUMBER|android.text.InputType.TYPE_NUMBER_FLAG_DECIMAL);form.addView(upper);
        TextView help=label("",12,MUTED);form.addView(help);
        Runnable controls=()->{int kind=fields.optJSONObject(field.getSelectedItemPosition()).optInt("kind");int index=op.getSelectedItemPosition();int code=index>=0&&index<codes[0].length?codes[0][index]:2;value.setVisibility(kind==3||code==8||code==9?View.GONE:View.VISIBLE);yesNo.setVisibility(kind==3?View.VISIBLE:View.GONE);upper.setVisibility(code==10?View.VISIBLE:View.GONE);};
        op.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> p){}public void onItemSelected(AdapterView<?> p,View v,int i,long id){controls.run();}});
        field.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> p){}public void onItemSelected(AdapterView<?> p,View v,int i,long id){
            JSONObject selected=fields.optJSONObject(i);String key=selected.optString("key");int kind=selected.optInt("kind");codes[0]=MobileFilters.operators(kind);String[] labels=new String[codes[0].length];for(int j=0;j<labels.length;j++)labels[j]=MobileFilters.operatorLabel(codes[0][j]);op.setAdapter(new ArrayAdapter<>(MainActivity.this,android.R.layout.simple_spinner_dropdown_item,labels));
            value.setInputType(kind==1?android.text.InputType.TYPE_CLASS_NUMBER|android.text.InputType.TYPE_NUMBER_FLAG_DECIMAL|android.text.InputType.TYPE_NUMBER_FLAG_SIGNED:android.text.InputType.TYPE_CLASS_TEXT);
            String initial=key.equals("bpm")?"140":key.equals("bitrate")?"320":key.equals("format")?"FLAC":key.equals("samplerate")?"44100":key.equals("rating")?"4":"";value.setText(initial,false);
            value.setHint(kind==2?"Datum: JJJJ-MM-TT":kind==1?"Zahl":"Wert eingeben oder Vorschlag wählen");
            TreeSet<String> suggestions=new TreeSet<>(String.CASE_INSENSITIVE_ORDER);
            if(key.equals("format"))suggestions.addAll(Arrays.asList("FLAC","MP3","WAV","AIFF","AAC","M4A","OGG","OPUS","ALAC"));
            if(key.equals("bpm"))suggestions.addAll(Arrays.asList("120","125","128","130","135","140","145","150","160","170","180"));
            if(key.equals("bitrate"))suggestions.addAll(Arrays.asList("96","128","160","192","256","320","1000"));
            String binding=key.equals("genre")?"genreRaw":key.equals("subgenre")?"subGenre":key.equals("key")?"musicalKey":key;
            for(JSONObject track:loaded){String s=track.isNull(binding)?"":track.optString(binding,"");if(!s.trim().isEmpty())suggestions.add(s);}
            value.setAdapter(new ArrayAdapter<>(MainActivity.this,android.R.layout.simple_dropdown_item_1line,new ArrayList<>(suggestions)));value.setOnClickListener(view->{if(!suggestions.isEmpty())value.showDropDown();});
            help.setText(kind==3?"Tags direkt mit Ja oder Nein filtern.":key.equals("bpm")?"BPM: z. B. mindestens 140 oder zwischen 130 und 150.":key.equals("bitrate")?"Originaldatei-Bitrate in kbps, unabhängig von der Streaming-Qualität.":key.equals("genre")?"Vorschläge aus den aktuell geladenen Songs; weitere Genres frei eingeben.":"Passenden Vergleich auswählen und den Filter hinzufügen.");controls.run();
        }});for(int i=0;i<fields.length();i++)if(fields.optJSONObject(i).optString("key").equals("bpm")){field.setSelection(i);break;}
        TextView error=label("",12,MUTED);form.addView(error);
        form.addView(button("+ Filter hinzufügen",()->{try{JSONObject selected=fields.optJSONObject(field.getSelectedItemPosition());String text=selected.optInt("kind")==3?(yesNo.getSelectedItemPosition()==0?"ja":"nein"):value.getText().toString();JSONArray added=MobileFilters.create(selected,codes[0][op.getSelectedItemPosition()],text,upper.getText().toString());for(int i=0;i<added.length();i++)draft[0].put(added.opt(i));error.setText("Filter hinzugefügt – mit Anwenden übernehmen.");redraw[0].run();}catch(IllegalArgumentException e){error.setText(e.getMessage());}}));
        ScrollView scroll=new ScrollView(this);scroll.addView(form);
        new AlertDialog.Builder(this).setTitle("Suchen, Filtern & Sortieren").setView(scroll).setPositiveButton("Anwenden",(d,n)->{
            sort=sorting.getSelectedItemPosition()==0?"default":fields.optJSONObject(sorting.getSelectedItemPosition()-1).optString("key");searchField=searching.getSelectedItemPosition()==0?"all":fields.optJSONObject(searching.getSelectedItemPosition()-1).optString("key");descending=desc.isChecked();
            filters=draft[0];api.settings.edit().putString("metadataFilters",filters.toString()).putString("trackSort",sort).putString("trackSearchField",searchField).putBoolean("trackDescending",descending).apply();
            if(source.kind<0)open(new Source("Collection",0,null,null),true);else reload(false);
        }).setNeutralButton("Filter löschen",(d,n)->{filters=new JSONArray();api.settings.edit().remove("metadataFilters").apply();reload(false);}).setNegativeButton("Abbrechen",null).show();
    }
    private void renderFavorites(){
        favoritesBox.removeAllViews();if(!favoritesExpanded)return;
        JSONArray profiles=favoriteData.optJSONArray("profiles");if(profiles==null||profiles.length()==0){favoritesBox.addView(label("Favoriten zuerst am PC einrichten.",12,MUTED));return;}
        String active=api.settings.getString("favoriteProfile",favoriteData.optString("activeProfileName",""));String[] names=new String[profiles.length()];int chosen=0;for(int i=0;i<profiles.length();i++){names[i]=profiles.optJSONObject(i).optString("name");if(names[i].equals(active))chosen=i;}
        Spinner profile=spinner(names);profile.setSelection(chosen);favoritesBox.addView(profile);final LinearLayout slots=row();HorizontalScrollView scroll=new HorizontalScrollView(this);scroll.addView(slots);favoritesBox.addView(scroll);
        profile.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> p){}public void onItemSelected(AdapterView<?> p,View v,int pos,long id){String name=names[pos];api.settings.edit().putString("favoriteProfile",name).apply();slots.removeAllViews();JSONArray entries=profiles.optJSONObject(pos).optJSONArray("slots");if(entries==null)return;for(int i=0;i<entries.length();i++){JSONObject s=entries.optJSONObject(i);slots.addView(button((s.optInt("index")+1)+" · "+s.optString("name"),()->{PlaybackService service=PlaybackService.live;if(service==null||service.current==null){show("Kein Song im Handy-Player geladen.");return;}long track=service.current.optLong("id");work(()->api.call("POST","/api/favorites/"+enc(name)+"/"+s.optInt("index"),Api.json("ids",new JSONArray().put(track))),true);}));}}});
    }
    private void settings(){
        LinearLayout form=column();form.setPadding(dp(16),dp(8),dp(16),0);EditText url=input(api.settings.getString("url",""),false);url.setHint("https://musik.deine-domain.de");url.setSingleLine(true);url.setInputType(android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_VARIATION_URI);form.addView(label("Server-Adresse · HTTPS funktioniert mit jedem Port",13,MUTED));form.addView(url);
        EditText port=input("5874",false);port.setSingleLine(true);port.setInputType(android.text.InputType.TYPE_CLASS_NUMBER);
        try{java.net.URI saved=new java.net.URI(api.settings.getString("url",""));if(saved.getPort()>0)port.setText(Integer.toString(saved.getPort()));else if(saved.getHost()!=null)port.setText("https".equalsIgnoreCase(saved.getScheme())?"443":"80");}catch(Exception ignored){}
        form.addView(label("Server-Port · muss zur Desktop-/Proxy-Einstellung passen",13,MUTED));form.addView(port);
        url.addTextChangedListener(new TextWatcher(){public void beforeTextChanged(CharSequence s,int a,int c,int f){}public void afterTextChanged(Editable e){}public void onTextChanged(CharSequence s,int a,int b,int c){try{int number=new java.net.URI(s.toString()).getPort();if(number>0)port.setText(Integer.toString(number));}catch(Exception ignored){}}});
        EditText key=input("",true);key.setHint(api.configured()?"Leer lassen = vorhandenen Schlüssel behalten":"Zugriffsschlüssel aus Cratebase");form.addView(key);
        CheckBox lan=new CheckBox(this);lan.setText("HTTP im eigenen LAN erlauben (private IP)");lan.setTextColor(TEXT);lan.setChecked(api.settings.getBoolean("lan",false));form.addView(lan);
        CheckBox internetHttp=new CheckBox(this);internetHttp.setText("HTTP über Internet erlauben (unverschlüsselt)");internetHttp.setTextColor(TEXT);internetHttp.setChecked(api.settings.getBoolean("internetHttp",false));form.addView(internetHttp);
        form.addView(label("Bei HTTP werden Zugriffsschlüssel, Musik und Änderungen unverschlüsselt übertragen. Adresse mit http:// verwenden. HTTPS bleibt ohne diesen Schalter erforderlich.",12,MUTED));
        internetHttp.setOnCheckedChangeListener((button,checked)->{if(checked&&url.getText().toString().startsWith("https://"))url.setText("http://"+url.getText().toString().substring(8));});
        String[] qualities={"96 kbit/s · ca. 43 MB/Stunde","160 kbit/s · ca. 72 MB/Stunde","256 kbit/s · ca. 115 MB/Stunde","320 kbit/s · ca. 144 MB/Stunde"};int[] rates={96,160,256,320};Spinner mobile=spinner(qualities),wifi=spinner(qualities);for(int i=0;i<rates.length;i++){if(api.settings.getInt("mobileQuality",160)==rates[i])mobile.setSelection(i);if(api.settings.getInt("wifiQuality",256)==rates[i])wifi.setSelection(i);}form.addView(label("Mobilfunk / getaktete Verbindung",13,MUTED));form.addView(mobile);form.addView(label("WLAN",13,MUTED));form.addView(wifi);
        form.addView(label("MP3-Streaming. Angaben ohne Netzwerk-Overhead. Neue Qualität gilt beim nächsten Start einer Wiedergabeliste. Kein Download der Original-FLAC.\n\nSperrbildschirm: Android-Benachrichtigungen für Cratebase erlauben und Inhalte anzeigen. Die zusätzliche Markierungsanzeige kann Club, Extended und Intro ohne Entsperren setzen.",12,MUTED));
        TextView connectionStatus=label("HTTP und HTTPS müssen zum Server passen. Ein HTTP-Server wird durch einen anderen Port nicht automatisch zu HTTPS.",12,MUTED);form.addView(connectionStatus);
        ScrollView scroll=new ScrollView(this);scroll.addView(form);
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle("Verbindung & Streaming").setView(scroll).setPositiveButton("Verbinden",null).setNegativeButton("Schließen",null).create();
        dialog.setOnShowListener(d->dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v->{try{
            String access=key.getText().toString().trim();if(access.isEmpty()&&api.configured())access=api.token();final String accessKey=access;
            final boolean allowLan=lan.isChecked(),allowInternetHttp=internetHttp.isChecked();final String validated=ConnectionPolicy.withPort(url.getText().toString(),port.getText().toString(),allowLan,allowInternetHttp);
            final int mobileRate=rates[mobile.getSelectedItemPosition()],wifiRate=rates[wifi.getSelectedItemPosition()];
            dialog.getButton(AlertDialog.BUTTON_POSITIVE).setEnabled(false);connectionStatus.setText("Verbindung und Collection werden geprüft …");
            Api.NETWORK.execute(()->{try{
                int count=api.checkConnection(validated,accessKey,allowLan,allowInternetHttp);
                main.post(()->{if(!dialog.isShowing())return;try{
                    boolean changed=!validated.equals(api.settings.getString("url",""))||!api.configured()||!accessKey.equals(api.token());
                    if(changed&&PlaybackService.live!=null)PlaybackService.live.disconnect();
                    api.configure(validated,accessKey,allowLan,allowInternetHttp);api.settings.edit().putInt("mobileQuality",mobileRate).putInt("wifiQuality",wifiRate).apply();dialog.dismiss();history.clear();source=Source.home();lastRevision=-1;reload(true);show("Verbunden · "+count+" Songs auf dem Server");
                }catch(Exception e){connectionStatus.setText(e.getMessage());dialog.getButton(AlertDialog.BUTTON_POSITIVE).setEnabled(true);}});
            }catch(Exception e){main.post(()->{connectionStatus.setText("Nicht verbunden: "+e.getMessage()+"\nAdresse, Port und HTTP/HTTPS am Desktop prüfen.");dialog.getButton(AlertDialog.BUTTON_POSITIVE).setEnabled(true);});}});
        }catch(Exception e){connectionStatus.setText(e.getMessage());show(e.getMessage());}}));dialog.show();
    }
    private void renderPlayer(){PlaybackService service=PlaybackService.live;if(service==null)return;JSONObject current=service.current;playerTitle.setText(current==null?"Musik zum Vorhören auswählen":current.optString("displayTitle")+" · "+current.optString("displayArtist"));LinearLayout flags=(LinearLayout)playerBox.getChildAt(playerBox.getChildCount()-1);for(int i=0;i<flags.getChildCount();i++){Button b=(Button)flags.getChildAt(i);String field=(String)b.getTag();boolean on=current!=null&&current.optBoolean(field.equals("club")?"clubReady":field);b.setText((on?"✓ ":"")+cap(field));b.setTextColor(on?GREEN:TEXT);b.setEnabled(current!=null);}if(!service.status.isEmpty()&&!service.status.equals(lastStatus)){lastStatus=service.status;show(service.status);}}
    private final Runnable tick=new Runnable(){public void run(){PlaybackService service=PlaybackService.live;if(service!=null){long duration=service.selectedDuration();long pos=service.selectedPosition();if(!seeking)seek.setProgress(duration>0?(int)(1000*pos/duration):0);position.setText(time(pos)+" / "+time(Math.max(0,duration)));playbackDevice.setText(service.remoteMode?"Ausgabe: Desktop ▾":"Ausgabe: Dieses Handy ▾");}main.postDelayed(this,1000);}};
    private void deviceDialog(){
        PlaybackService service=PlaybackService.live;if(service==null){show("Player startet noch.");return;}
        if(service.switchingDevice){show("Gerätewechsel läuft …");return;}
        new AlertDialog.Builder(this).setTitle("Wiedergabegerät").setItems(new String[]{"Dieses Handy · hier weiterhören","Desktop · laufenden PC-Player fernsteuern","Desktop · aktuellen Handy-Song übernehmen","Desktop-Lautstärke …"},(d,index)->{
            if(index==0)service.selectPhone();else if(index==1)service.selectDesktop(false);else if(index==2)service.selectDesktop(true);
            else{if(!service.remoteMode){show("Zuerst Desktop auswählen.");return;}SeekBar volume=new SeekBar(this);volume.setMax(100);volume.setProgress(service.desktopVolume());new AlertDialog.Builder(this).setTitle("Desktop-Lautstärke").setView(volume).setPositiveButton("Übernehmen",(a,b)->service.setDesktopVolume(volume.getProgress())).setNegativeButton("Abbrechen",null).show();}
        }).setNegativeButton("Abbrechen",null).show();
    }
    private boolean polling;
    private final Runnable revisionPoll=new Runnable(){public void run(){if(api.configured()&&!polling&&!loading){polling=true;PlaybackService.IO.execute(()->{try{long revision=api.get("/api/status").optLong("revision");main.post(()->{if(revision!=lastRevision){boolean refresh=lastRevision!=-1;lastRevision=revision;if(refresh)reload(true);}});}catch(Exception ignored){}finally{main.post(()->polling=false);}});}main.postDelayed(this,5000);}};
    @Override public void onStart(){super.onStart();androidx.core.content.ContextCompat.registerReceiver(this,updates,new IntentFilter(PlaybackService.UPDATE),androidx.core.content.ContextCompat.RECEIVER_NOT_EXPORTED);main.post(tick);main.post(revisionPoll);}
    @Override public void onStop(){unregisterReceiver(updates);main.removeCallbacks(tick);main.removeCallbacks(revisionPoll);super.onStop();}
    @Override public void onDestroy(){main.removeCallbacksAndMessages(null);if(controllerFuture!=null)MediaController.releaseFuture(controllerFuture);super.onDestroy();}
    private void send(String action){PlaybackService service=PlaybackService.live;if(service==null){show("Player wird gestartet …");return;}startService(new Intent(this,PlaybackService.class).setAction(action));}
    private interface Work{Object run()throws Exception;}
    private void work(Work work,boolean refresh){show("Wird gespeichert …");PlaybackService.IO.execute(()->{try{work.run();main.post(()->{show("Gespeichert");if(refresh){selected.clear();reload(true);}if(PlaybackService.live!=null)PlaybackService.live.publish();});}catch(Exception e){main.post(()->show("Nicht gespeichert: "+e.getMessage()));}});}
    private void show(String text){message.setText(text==null?"Unbekannter Fehler":text);}
    private LinearLayout column(){LinearLayout v=new LinearLayout(this);v.setOrientation(LinearLayout.VERTICAL);return v;}
    private LinearLayout row(){LinearLayout v=new LinearLayout(this);v.setOrientation(LinearLayout.HORIZONTAL);v.setGravity(Gravity.CENTER_VERTICAL);return v;}
    private TextView label(String text,int size,int color){TextView v=new TextView(this);v.setText(text);v.setTextColor(color);v.setTextSize(size);v.setPadding(0,dp(3),0,dp(3));return v;}
    private Button button(String text,Runnable action){Button b=new Button(this);b.setText(text);b.setTextSize(12);b.setTextColor(TEXT);b.setAllCaps(false);b.setMinWidth(0);b.setMinimumWidth(0);b.setPadding(dp(8),0,dp(8),0);b.setOnClickListener(v->action.run());return b;}
    private EditText input(String value,boolean secret){EditText t=new EditText(this);t.setTextColor(TEXT);t.setHintTextColor(MUTED);t.setText(value);t.setTextSize(14);if(secret)t.setInputType(android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD);return t;}
    private Spinner spinner(String[] values){Spinner s=new Spinner(this);ArrayAdapter<String> adapter=new ArrayAdapter<>(this,android.R.layout.simple_spinner_dropdown_item,values);s.setAdapter(adapter);return s;}
    private int dp(int value){return Math.round(getResources().getDisplayMetrics().density*value);}
    private static String cap(String text){return text.substring(0,1).toUpperCase(Locale.GERMAN)+text.substring(1);}
    private static String enc(String text){try{return URLEncoder.encode(text==null?"":text,"UTF-8").replace("+","%20");}catch(java.io.UnsupportedEncodingException e){throw new IllegalStateException(e);}}
    private static String time(long millis){long s=millis/1000;return String.format(Locale.ROOT,"%d:%02d",s/60,s%60);}
}
