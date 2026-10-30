"""Small Windows control panel for the private Traktor live-stem experiment."""
import json,os,queue,shutil,subprocess,sys,threading,time
from pathlib import Path
import tkinter as tk
import xml.etree.ElementTree as ET
from tkinter import ttk,filedialog,messagebox
import psutil
from add_live_tracks import prepare,atomic_json,lab_root
from gpu_service import request
from live_settings import read as read_live_settings,save as save_live_settings
from collection_store import ensure_collection,import_collection,default_source,read_state,private_path,sync_collection
from profile_lock import hold as hold_profile
from sync_control import Runtime as SyncRuntime
from runtime_install import inference_python,read as read_runtime_install

LAB=lab_root();SCRIPTS=Path(__file__).resolve().parent
PYTHON=LAB/'python'/'Scripts'/'python.exe';CUDA=inference_python(LAB)
PRIVATE_COLLECTION=private_path(LAB)
SETTINGS=LAB/'live-stems-settings.json'

def running():
    return [p for p in psutil.process_iter(['name','exe']) if (p.info['name'] or '').lower()=='traktor pro 4.exe']
def read_settings():
    return read_live_settings(SETTINGS)
def hidden(command,log):
    stream=log.open('a',encoding='utf-8')
    try:return subprocess.Popen(command,cwd=SCRIPTS,stdin=subprocess.DEVNULL,stdout=stream,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
    finally:stream.close()

def prepare_launch_settings():
    # Only isolated test preferences; preserve controller mappings and native effects.
    path=PRIVATE_COLLECTION.with_name('Traktor Settings.tsi');tree=ET.parse(path)
    recordings=LAB/'recordings';recordings.mkdir(exist_ok=True)
    # Runtime eligibility follows the enable switch; keep the native default on
    # so enabling during a session also affects the next ordinary deck load.
    changes={'Browser.Dir.GeneratedStems':str(LAB/'stems'),'Browser.Dir.Recordings':str(recordings),'Stems.DefaultLoad.Stems':'1'}
    for entry in tree.iter('Entry'):
        if entry.get('Name') in changes:entry.set('Value',changes[entry.get('Name')])
    tree.write(path,encoding='utf-8',xml_declaration=True)

class Control:
    def __init__(self):
        self.root=tk.Tk();self.root.title('Cratebase · Traktor Live Stems (Experiment)')
        self.root.geometry(f'{min(900,max(760,self.root.winfo_screenwidth()-80))}x{min(900,max(500,self.root.winfo_screenheight()-100))}');self.root.minsize(760,500)
        self.events=queue.Queue();self.busy=False;self.launcher=None;self.started=None;self.start_reported=False
        self.sync_runtime=SyncRuntime(LAB)
        style=ttk.Style();style.theme_use('clam');style.configure('.',background='#20242b',foreground='#e5e9f0',font=('Segoe UI',10));style.configure('Treeview',background='#171a20',fieldbackground='#171a20',rowheight=29);style.map('Treeview',background=[('selected','#355a77')]);style.map('TCombobox',fieldbackground=[('readonly','#343b46')],foreground=[('readonly','#e5e9f0')]);self.root.configure(bg='#20242b')
        style.map('TCheckbutton',background=[('active','#303742')],foreground=[('active','#e5e9f0')])
        # Handheld screens at 125–150% scaling need access to controls below
        # the fold rather than a window whose minimum height exceeds the screen.
        canvas=tk.Canvas(self.root,bg='#20242b',highlightthickness=0)
        scrollbar=ttk.Scrollbar(self.root,orient='vertical',command=canvas.yview)
        canvas.configure(yscrollcommand=scrollbar.set);scrollbar.pack(side='right',fill='y');canvas.pack(side='left',fill='both',expand=True)
        outer=ttk.Frame(canvas,padding=18);window=canvas.create_window((0,0),window=outer,anchor='nw')
        outer.bind('<Configure>',lambda event:canvas.configure(scrollregion=canvas.bbox('all')))
        canvas.bind('<Configure>',lambda event:canvas.itemconfigure(window,width=event.width))
        ttk.Label(outer,text='Traktor · Live Stems',font=('Segoe UI',20,'bold')).pack(anchor='w')
        ttk.Label(outer,text='Private Kopie · Traktor Pro 4.5.1 · GPU/CPU wird automatisch erkannt',foreground='#87c8ff').pack(anchor='w',pady=(2,10))
        ttk.Label(outer,text='Track in ein Stem-Deck laden → GPU trennt Drums, Bass, Other und Vocals.\nBis Daten bereitstehen, bleibt der Originalmix verfügbar. Traktor benötigt beim Start bis zu zwei Minuten.',wraplength=800).pack(anchor='w')
        bar=ttk.Frame(outer);bar.pack(fill='x',pady=14)
        self.start_button=ttk.Button(bar,text='Traktor mit Live Stems starten',command=self.start);self.start_button.pack(side='left')
        ttk.Button(bar,text='LAN-Synchronisierung…',command=lambda:self.sync_runtime.show(self.root)).pack(side='left',padx=8)
        collection_bar=ttk.LabelFrame(outer,text='Traktor-Collection · private Kopie',padding=8);collection_bar.pack(fill='x',pady=(0,10))
        state=read_state(LAB)
        self.collection_label=tk.StringVar(value=self.describe_collection(state) if state else 'Beim nächsten Start wird deine originale Traktor-Collection übernommen.')
        ttk.Label(collection_bar,textvariable=self.collection_label,wraplength=810).pack(anchor='w')
        actions=ttk.Frame(collection_bar);actions.pack(anchor='w',pady=(7,0))
        ttk.Button(actions,text='Collection wählen…',command=lambda:self.import_library(True)).pack(side='left')
        ttk.Button(actions,text='Original erneut übernehmen',command=lambda:self.import_library(False)).pack(side='left',padx=8)
        ttk.Button(actions,text='Edition ins Original übernehmen',command=self.export_library).pack(side='left')
        self.collection_sync_status=tk.StringVar(value='Collection-Abgleich: wartet auf geschlossene Traktor-Instanzen')
        ttk.Label(collection_bar,textvariable=self.collection_sync_status,wraplength=810).pack(anchor='w',pady=(5,0))
        values=read_settings();self.enabled=tk.BooleanVar(value=values.get('enabled',True))
        settings=ttk.LabelFrame(outer,text='GPU und Leistung',padding=10);settings.pack(fill='x')
        ttk.Checkbutton(settings,text='Live Stems aktivieren · normale Tracks automatisch als Stems laden',variable=self.enabled,command=self.save).grid(row=0,column=0,columnspan=7,sticky='w',pady=(0,10))
        self.precision=tk.StringVar(value=values.get('precision','fp16'));self.budget=tk.StringVar(value=str(values.get('memory_budget_mb',4096)));self.limit=tk.StringVar(value=str(values.get('max_active_tracks',4)))
        for col,(label,variable,options) in enumerate((('Genauigkeit',self.precision,['fp16','fp32']),('GPU-Budget (MB)',self.budget,['2048','3072','4096','5120','6144']),('Gleichzeitige Aufträge',self.limit,['1','2','3','4']))):
            ttk.Label(settings,text=label).grid(row=1,column=col*2,padx=(0,7));ttk.Combobox(settings,textvariable=variable,values=options,state='readonly',width=9).grid(row=1,column=col*2+1,padx=(0,18))
        ttk.Button(settings,text='Speichern',command=self.save).grid(row=1,column=6)
        backend=read_runtime_install(LAB).get('backend','cuda')
        ttk.Label(settings,text=f'Backend: {backend.upper()} · FP16 und GPU-Budget gelten nur für CUDA. DirectML nutzt FP32 und den Windows-Speichermanager.',foreground='#b3bbc8',wraplength=800).grid(row=2,column=0,columnspan=7,sticky='w',pady=(8,0))
        self.status=tk.StringVar(value='GPU-Dienst wird geprüft…');ttk.Label(outer,textvariable=self.status,wraplength=820).pack(fill='x',pady=12)
        self.runtime_status=tk.StringVar(value='Traktor: noch keine aktuelle Bestätigung')
        ttk.Label(outer,textvariable=self.runtime_status,foreground='#87c8ff',wraplength=820).pack(fill='x',pady=(0,8))
        self.table=ttk.Treeview(outer,columns=('track','phase','progress'),show='headings',height=7)
        for name,title,width in [('track','Track',470),('phase','Status',150),('progress','Fortschritt',110)]:self.table.heading(name,text=title);self.table.column(name,width=width,minwidth=70)
        self.table.pack(fill='both',expand=True)
        self.notice=tk.StringVar(value='Aktiv: Songs aus Ordnern, Collection und Playlisten normal laden → Stem-Deck.\nDeaktiviert: neue Songs normal laden; bereits geladene Live-Stem-Decks erhalten den Originalmix auf vier Spuren. Spur-Mutes, Filter und Fader bleiben wirksam. Für den vollständigen Originalmix alle vier Spuren einschalten.')
        ttk.Label(outer,textvariable=self.notice,wraplength=820,foreground='#b3bbc8').pack(anchor='w',pady=(12,0))
        self.root.after(100,self.drain);threading.Thread(target=self.poll,daemon=True).start()
    def work(self,fn):
        def run():
            try:fn()
            except Exception as e:self.events.put(('error',str(e)))
        threading.Thread(target=run,daemon=True).start()
    @staticmethod
    def describe_collection(state):
        return f"{state['tracks']:,} Tracks · {state['playlists']:,} Playlisten\n{state['source']}"
    def import_library(self,choose):
        if self.busy:return
        if running():messagebox.showinfo('Collection übernehmen','Bitte Traktor zuerst schließen, damit seine geladene Collection nicht überschrieben wird.');return
        source=default_source(LAB)
        if choose:
            selected=filedialog.askopenfilename(title='Originale Traktor-Collection auswählen',initialdir=source.parent,filetypes=[('Traktor Collection','*.nml')])
            if not selected:return
            source=Path(selected)
        self.busy=True;self.start_button.configure(state='disabled')
        self.notice.set('Collection wird geprüft und als private Kopie übernommen…')
        def run():
            try:
                state=import_collection(LAB,source,lambda:bool(running()))
                self.events.put(('collection',state))
                self.events.put(('notice','Collection übernommen. Beim nächsten Start stehen ihre Tracks und Playlisten zur Verfügung. Die bisherige private Collection wurde gesichert.'))
            finally:self.events.put(('idle',None))
        self.work(run)
    def save(self):
        try:
            values=read_settings();values.update(enabled=self.enabled.get(),precision=self.precision.get(),memory_budget_mb=int(self.budget.get()),max_active_tracks=int(self.limit.get()));save_live_settings(SETTINGS,values)
        except (OSError,ValueError) as error:
            messagebox.showerror('Einstellungen konnten nicht gespeichert werden',str(error));return False
        self.notice.set('Gespeichert · Bestätigung der laufenden Traktor-Testkopie wird geprüft…')
        def update():
            try:
                result=request({'command':'settings','values':{k:values[k] for k in ('enabled','precision','memory_budget_mb','max_active_tracks')}})
                if not result.get('ok'):raise RuntimeError(result.get('error'))
            except (FileNotFoundError,ConnectionError,OSError):pass
            self.events.put(('notice','Einstellungen gespeichert. Die Statuszeile zeigt, ob die laufende Testkopie die Umschaltung bestätigt hat.'))
        self.work(update)
        return True
    def export_library(self):
        if running():messagebox.showinfo('Collection übernehmen','Traktor bitte zuerst schließen.');return
        def export():
            with hold_profile(LAB):result=sync_collection(LAB,lambda:bool(running()),prefer='edition')
            self.events.put(('notice','Collection-Abgleich: '+result['status']+'. Der vorherige Dateistand ist gesichert.'))
        self.work(export)
    def start(self):
        if self.busy:return
        if running():messagebox.showinfo('Traktor läuft bereits','Bitte Traktor zuerst schließen. Die Live-Erweiterung wird beim Start der privaten Testkopie geladen.');return
        self.busy=True;self.start_button.configure(state='disabled')
        if not self.save():self.busy=False;self.start_button.configure(state='normal');return
        def launch():
            try:
                try:state=request({'command':'status'})
                except OSError:
                    hidden([str(CUDA),str(SCRIPTS/'gpu_service.py'),'serve'],LAB/'logs'/'gpu-service-console.log');state={'phase':'loading'}
                if state.get('inference_python') and Path(state['inference_python']).resolve()!=CUDA.resolve():
                    # start() already refuses to switch while Traktor is running.
                    request({'command':'shutdown'})
                    for _ in range(100):
                        time.sleep(.1)
                        try:request({'command':'status'})
                        except OSError:break
                    else:raise RuntimeError('Alter GPU-Dienst beendet sich nicht. Bitte schließen und erneut versuchen.')
                    hidden([str(CUDA),str(SCRIPTS/'gpu_service.py'),'serve'],LAB/'logs'/'gpu-service-console.log');state={'phase':'loading'}
                deadline=time.monotonic()+120
                while state.get('phase')!='ready':
                    if state.get('phase')=='error':raise RuntimeError(state.get('error') or 'GPU-Dienst fehlgeschlagen')
                    if time.monotonic()>deadline:raise RuntimeError('GPU-Modell wurde innerhalb von zwei Minuten nicht bereit. Siehe GPU-Protokoll.')
                    self.events.put(('notice','GPU-Modell wird geladen…'));time.sleep(1)
                    try:state=request({'command':'status'})
                    except OSError:continue
                if running():raise RuntimeError('Traktor wurde zwischenzeitlich gestartet. Bitte zuerst schließen.')
                self.events.put(('notice','Traktor-Collection wird vorbereitet…'))
                self.events.put(('collection',ensure_collection(LAB,lambda:bool(running()))))
                with hold_profile(LAB):prepare_launch_settings()
                self.launcher=hidden([str(PYTHON),str(SCRIPTS/'launch_lab.py'),'--live','--favorites'],LAB/'logs'/'live-launch-console.log');self.started=time.monotonic();self.start_reported=False
                self.events.put(('notice','Traktor wird gestartet. Bitte bis zu zwei Minuten warten, bis die Vollbildansicht geladen ist.'))
            finally:self.events.put(('idle',None))
        self.work(launch)
    def add(self):
        if self.busy:return
        files=filedialog.askopenfilenames(title='Songs für private Live-Stem-Testcollection',filetypes=[('Musik','*.mp3 *.flac *.wav *.aiff *.aif *.m4a *.ogg'),('Alle Dateien','*.*')])
        if not files:return
        self.busy=True
        def add():
            try:
                collection=Path.home()/'Documents'/'Native Instruments'/'Traktor 4.5.1'/'collection.nml'
                added=prepare(files,collection,onprogress=lambda text:self.events.put(('notice',text)))
                self.events.put(('notice',f'{len(added)} Songs hinzugefügt. Nach Schließen von Traktor hier erneut starten; Playlist „Live Stems – hinzugefügt“ öffnen.'))
            finally:self.events.put(('idle',None))
        self.work(add)
    def poll(self):
        while True:
            if self.launcher and self.launcher.poll() is not None:
                self.events.put(('notice','Traktor-Testkopie beendet. Bei einem unerwarteten Ende siehe logs/live-launch-console.log im privaten Arbeitsbereich.'));self.launcher=None
            elif self.launcher and self.started and not self.start_reported and time.monotonic()-self.started>=125:
                self.start_reported=True;self.events.put(('notice','Testkopie läuft. Einen Song aus Ordnern, Collection oder Playlisten normal laden. Aktivieren/Deaktivieren ist ohne Traktor-Neustart möglich.'))
            try:
                runtime=json.loads((LAB/'live-runtime-status.json').read_text(encoding='utf-8'))
                active=json.loads((LAB/'active-session.json').read_text(encoding='utf-8'))
                if runtime.get('pid')!=active.get('pid') or time.time()-runtime.get('updated',0)>10 or not psutil.pid_exists(runtime.get('pid',-1)):runtime=None
            except (OSError,ValueError):runtime=None
            self.events.put(('runtime',runtime))
            try:self.events.put(('snapshot',request({'command':'status'})))
            except OSError:self.events.put(('offline',None))
            time.sleep(2)
    def drain(self):
        self.collection_sync_status.set(self.sync_runtime.collection_status)
        try:
            while True:
                kind,value=self.events.get_nowait()
                if kind=='error':self.notice.set(value);messagebox.showerror('Live Stems',value)
                elif kind=='notice':self.notice.set(value)
                elif kind=='collection':self.collection_label.set(self.describe_collection(value))
                elif kind=='idle':self.busy=False;self.start_button.configure(state='normal')
                elif kind=='offline':self.status.set('GPU-Dienst aus · wird beim Start automatisch geladen')
                elif kind=='runtime':
                    self.runtime_status.set('Traktor: '+(('Live Stems aktiv' if value['enabled'] else 'Live Stems deaktiviert')+' · bestätigt' if value and value.get('enabled')==self.enabled.get() else 'keine aktuelle Bestätigung der gewählten Einstellung'))
                elif kind=='snapshot':
                    labels={'ready':'bereit','loading':'wird geladen','complete':'fertig','queued':'wartet','separating':'Trennung läuft','decoding':'Audio wird gelesen','error':'Fehler','cancelled':'abgebrochen'}
                    self.status.set(f"GPU: {value.get('gpu') or 'wird geladen'} · {labels.get(value.get('phase'),value.get('phase'))} · Live-Trennung {'aktiv' if value.get('settings',{}).get('enabled') else 'deaktiviert'}")
                    for item in self.table.get_children():self.table.delete(item)
                    for identity,job in value.get('jobs',{}).items():
                        total=job.get('total_blocks',0);ready=job.get('ready_blocks',0);progress=f'{ready/total:.0%}' if total else '—'
                        self.table.insert('', 'end',iid=identity,values=(Path(job.get('source','')).name,job.get('error') or labels.get(job.get('phase'),job.get('phase')),progress))
        except queue.Empty:pass
        self.root.after(200,self.drain)

if __name__=='__main__':
    if os.name=='nt':
        import ctypes
        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID('Cratebase.TraktorLiveStemsLab')
    Control().root.mainloop()
