"""German LAN pairing/settings panel used by the edition launcher."""
import json,queue,secrets,socket,threading,time,uuid
from pathlib import Path
import tkinter as tk
from tkinter import ttk,filedialog,messagebox
import psutil
from network_sync import Profile,Endpoint,Discovery,digest
from profile_config import directory
from live_settings import atomic_write
from profile_lock import hold as hold_profile,ProfileBusy
from collection_store import sync_collection


def traktor_running():return any((p.info['name'] or '').lower()=='traktor pro 4.exe' for p in psutil.process_iter(['name']))


class Runtime:
    def __init__(self,lab):
        self.lab=Path(lab);self.path=self.lab/'network-sync-settings.json';self.events=queue.Queue();self.endpoint=None;self.discovery=None;self.window=None
        self.config=json.loads(self.path.read_text(encoding='utf-8')) if self.path.exists() else {'enabled':False,'root':str(directory(lab)),'port':42518,'discovery_port':42519,'key':secrets.token_hex(32),'node':uuid.uuid4().hex,'name':socket.gethostname(),'manual_host':'','manual_port':42518}
        self.status='Deaktiviert';self.collection_status='Noch nicht abgeglichen';self.conflicts=[];self.operation=threading.Lock();self.halt=threading.Event()
        if self.config['enabled']:
            try:self.start()
            except Exception as error:self.status=str(error)
        threading.Thread(target=self.loop,daemon=True).start()

    def start(self):
        if self.endpoint:return
        cfg=self.config;key=bytes.fromhex(cfg['key'])
        profile=Profile(cfg['root'],self.lab/'network-sync',traktor_running,gate=self.lab)
        endpoint=Endpoint(profile,key,cfg['node'],cfg['name'])
        try:
            port=endpoint.start(port=int(cfg['port']))
            discovery=Discovery(endpoint,int(cfg['discovery_port']),port)
        except Exception:endpoint.close();raise
        self.endpoint=endpoint;self.discovery=discovery;self.status='Aktiv · gekoppelte PCs werden im LAN gesucht'

    def stop(self):
        if self.discovery:self.discovery.close();self.discovery=None
        if self.endpoint:self.endpoint.close();self.endpoint=None
        self.status='Deaktiviert'

    def save(self,values):
        cfg={**self.config,**values}
        if len(bytes.fromhex(cfg['key']))!=32:raise ValueError('Kopplungsschlüssel: 64 Hex-Zeichen erforderlich')
        for field in ('port','discovery_port','manual_port'):
            cfg[field]=int(cfg[field])
            if not 1024<=cfg[field]<=65535:raise ValueError('Ports müssen zwischen 1024 und 65535 liegen')
        if not Path(cfg['root']).is_dir():raise ValueError('Profilordner nicht gefunden')
        with self.operation:
            self.stop();self.config=cfg
            if cfg['enabled']:self.start()
            atomic_write(self.path,cfg)

    def peers(self):
        discovery=self.discovery
        result=discovery.snapshot() if discovery else []
        result=[p for p in result if time.time()-p['seen']<30]
        if self.config.get('manual_host'):result.append({'host':self.config['manual_host'],'port':int(self.config['manual_port']),'name':'Manuell eingetragen'})
        return result

    def sync(self):
        if not self.operation.acquire(blocking=False):return
        try:
            if not self.endpoint:return
            if traktor_running():self.status='Wartet: Traktor bitte zum Abgleichen schließen';return
            peers=self.peers()
            if not peers:self.status='Aktiv · noch kein gekoppelter PC gefunden';return
            all_conflicts=[]
            for peer in peers:
                try:
                    result=self.endpoint.sync(peer['host'],peer['port'])
                    all_conflicts.extend({**c,'host':peer['host'],'port':peer['port']} for c in result['conflicts'])
                    self.status=f"{peer['name']}: {result['copied']} Dateien übertragen · {len(result['conflicts'])} Konflikte"
                except Exception as error:self.status=f"{peer['name']}: {error}"
            self.conflicts=all_conflicts
        finally:self.operation.release()

    def resolve(self,item,use_local):
        with self.operation:
            if not self.endpoint:raise RuntimeError('Synchronisierung ist nicht aktiv')
            name=item['path'];profile=self.endpoint.profile
            if use_local:
                data=profile.read(name)
                if digest(data)!=item['local']:raise RuntimeError('Lokale Datei wurde inzwischen geändert; erneut abgleichen')
                self.endpoint.request(item['host'],item['port'],'/file?path='+__import__('urllib.parse',fromlist=['quote']).quote(name,safe=''),'PUT',data,item['remote'])
            else:
                data=self.endpoint.request(item['host'],item['port'],'/file?path='+__import__('urllib.parse',fromlist=['quote']).quote(name,safe=''))
                if digest(data)!=item['remote']:raise RuntimeError('Andere Datei wurde inzwischen geändert; erneut abgleichen')
                profile.write(name,data,item['local'])
            self.conflicts=[other for other in self.conflicts if other!=item];self.status='Konflikt übernommen; vorherige Datei gesichert'

    def loop(self):
        while not self.halt.wait(20):
            try:
                if not traktor_running():
                    with hold_profile(self.lab):result=sync_collection(self.lab,traktor_running)
                    labels={'equal':'Collection aktuell','exported':'Änderungen ins Original übernommen','imported':'Änderungen aus Traktor übernommen','conflict':'Collection-Konflikt: Version auswählen','suspicious_shrink':'Collection stark verkleinert: Übernahme angehalten','missing':'Collection-Datei fehlt'}
                    self.collection_status=labels.get(result['status'],result['status'])
            except ProfileBusy:pass
            except Exception as error:self.collection_status=str(error)
            self.sync()

    def show(self,parent):
        if self.window and self.window.winfo_exists():self.window.deiconify();self.window.lift();return
        window=self.window=tk.Toplevel(parent);window.title('Cratebase Edition · LAN-Synchronisierung');window.geometry('900x700')
        outer=ttk.Frame(window,padding=16);outer.pack(fill='both',expand=True)
        ttk.Label(outer,text='Traktor auf deinen PCs synchronisieren',font=('Segoe UI',16,'bold')).pack(anchor='w')
        ttk.Label(outer,text='Auf jedem PC denselben Kopplungsschlüssel eintragen. Übertragung erfolgt im LAN, sobald Traktor geschlossen ist.\nCollection, History und Analysedaten werden abgeglichen; Audiogerät und lokale TSI-Pfade bleiben pro PC erhalten.',wraplength=850).pack(anchor='w',pady=10)
        enabled=tk.BooleanVar(value=self.config['enabled']);ttk.Checkbutton(outer,text='LAN-Synchronisierung aktivieren',variable=enabled).pack(anchor='w')
        fields={};grid=ttk.Frame(outer);grid.pack(fill='x',pady=10)
        for row,(name,label) in enumerate((('root','Synchronisierter Ordner'),('name','Name dieses PCs'),('port','Daten-Port (TCP)'),('discovery_port','Erkennungs-Port (UDP)'),('key','Gemeinsamer Kopplungsschlüssel'),('manual_host','PC-Adresse optional'),('manual_port','Port des manuellen PCs'))):
            ttk.Label(grid,text=label).grid(row=row,column=0,sticky='w',pady=4)
            variable=fields[name]=tk.StringVar(value=str(self.config[name]))
            ttk.Entry(grid,textvariable=variable,width=64,show='•' if name=='key' else '').grid(row=row,column=1,sticky='ew',padx=8)
        grid.columnconfigure(1,weight=1)
        def browse():
            chosen=filedialog.askdirectory(parent=window,initialdir=fields['root'].get())
            if chosen:fields['root'].set(chosen)
        ttk.Button(grid,text='Wählen…',command=browse).grid(row=0,column=2)
        def copy_key():window.clipboard_clear();window.clipboard_append(fields['key'].get())
        ttk.Button(grid,text='Kopieren',command=copy_key).grid(row=4,column=2)
        status=tk.StringVar(value=self.status);ttk.Label(outer,textvariable=status,wraplength=850).pack(anchor='w',pady=8)
        peers=tk.StringVar();ttk.Label(outer,textvariable=peers,wraplength=850).pack(anchor='w')
        table=ttk.Treeview(outer,columns=('path','state'),show='headings',height=8);table.heading('path',text='Konfliktdatei');table.heading('state',text='Status');table.column('path',width=580);table.column('state',width=200);table.pack(fill='both',expand=True,pady=10)
        displayed={}
        def work(action):
            def run():
                try:action()
                except Exception as error:self.status=str(error)
            threading.Thread(target=run,daemon=True).start()
        def save():
            values={name:v.get() for name,v in fields.items()};values['enabled']=enabled.get();work(lambda:self.save(values))
        def resolve(local):
            selected=table.selection()
            if selected:
                item=displayed.get(selected[0])
                if item:work(lambda:self.resolve(item.copy(),local))
        buttons=ttk.Frame(outer);buttons.pack(fill='x')
        for title,action in [('Speichern',save),('Jetzt abgleichen',lambda:work(self.sync)),('Meine Version verwenden',lambda:resolve(True)),('Andere Version verwenden',lambda:resolve(False))]:ttk.Button(buttons,text=title,command=action).pack(side='left',padx=3)
        ttk.Label(outer,text='Gleichzeitige Änderungen erfordern eine Auswahl. Fehlende Dateien werden nicht automatisch gelöscht.\nDateien über 256 MB werden ausgelassen. Alte Dateistände liegen unter LocalAppData/Cratebase/traktor-lab/network-sync/backups.',wraplength=850).pack(anchor='w',pady=10)
        def refresh():
            if not window.winfo_exists():return
            status.set(self.status);peers.set('Gefunden: '+(', '.join(p['name'] for p in self.peers()) or 'keine gekoppelten PCs'))
            if not table.selection():
                table.delete(*table.get_children())
                displayed.clear()
                for i,item in enumerate(self.conflicts):
                    displayed[str(i)]=item.copy();table.insert('', 'end',iid=str(i),values=(item['path'],'Beide Versionen erhalten'))
            window.after(1000,refresh)
        refresh()
