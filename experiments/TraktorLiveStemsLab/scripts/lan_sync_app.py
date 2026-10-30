"""Portable companion entry point for PCs without the GPU experiment runtime."""
import os,re
from pathlib import Path
import tkinter as tk
from tkinter import ttk
from sync_control import Runtime


def main():
    lab=Path(os.environ.get('LOCALAPPDATA',Path.home()/'.local/share'))/'Cratebase/traktor-lab'
    lab.mkdir(parents=True,exist_ok=True)
    app=tk.Tk();app.title('Cratebase · Traktor LAN Sync');app.geometry('500x160')
    runtime=Runtime(lab)
    if not runtime.path.exists() and not Path(runtime.config['root']).is_dir():
        documents=Path.home()/'Documents'
        if os.name=='nt':
            import winreg
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER,r'Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders') as key:
                documents=Path(os.path.expandvars(winreg.QueryValueEx(key,'Personal')[0]))
        native=documents/'Native Instruments'
        candidates=[p for p in native.glob('Traktor *') if p.is_dir() and (p/'collection.nml').exists()]
        if candidates:runtime.config['root']=str(max(candidates,key=lambda p:tuple(map(int,re.findall(r'\d+',p.name)))))
        elif native.exists():runtime.config['root']=str(native)
    ttk.Label(app,text='Cratebase · Collection-Synchronisierung',font=('Segoe UI',15,'bold')).pack(pady=16)
    ttk.Button(app,text='Kopplung, Einstellungen und Konflikte öffnen',command=lambda:runtime.show(app)).pack()
    ttk.Label(app,text='Dieses Fenster während der Synchronisierung geöffnet lassen.').pack(pady=12)
    runtime.show(app)
    def close():
        runtime.halt.set();runtime.stop();app.destroy()
    app.protocol('WM_DELETE_WINDOW',close);app.mainloop()


if __name__=='__main__':main()
