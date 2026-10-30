"""Opt-in, authenticated LAN profile replication with per-peer three-way state.

No incoming writes while Traktor is running; no automatic deletions. Both
versions survive concurrent edits. Audio-device and local path TSI entries
remain local. Pairing is a shared random key, not an unauthenticated discovery.
"""
import copy
import hashlib
import hmac
import http.client
import json
import os
import secrets
import socket
import threading
import time
import uuid
import xml.etree.ElementTree as ET
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path, PurePosixPath
from urllib.parse import urlsplit, parse_qs, quote
from live_settings import atomic_write
from contextlib import nullcontext
from profile_lock import hold as hold_profile

MAX_FILE=256*1024*1024
LOCAL_FIELDS={'Browser.Dir.Root','Browser.Dir.Music','Browser.Dir.Recordings','Browser.Dir.GeneratedStems','Browser.Dir.ITunesMusic','Browser.Dir.ContentImportRoot','Browser.Dir.Loops'}


def local_entry(entry):
    name=entry.get('Name','')
    return name.startswith('Audio.') or name in LOCAL_FIELDS


def portable(data,name):
    if not name.lower().endswith('.tsi'):return data
    root=ET.fromstring(data)
    for parent in root.iter():
        for child in list(parent):
            if child.tag=='Entry' and local_entry(child):parent.remove(child)
    return ET.canonicalize(ET.tostring(root,encoding='unicode')).encode('utf-8')


def install_bytes(data,name,existing):
    if name.lower().endswith('.nml'):ET.fromstring(data)
    if not name.lower().endswith('.tsi'):return data
    root=ET.fromstring(data)
    if existing:
        own=ET.fromstring(existing)
        parent=next((p for p in root.iter() if any(c.tag=='Entry' for c in p)),root)
        for entry in own.iter('Entry'):
            if local_entry(entry):parent.append(copy.deepcopy(entry))
    return ET.tostring(root,encoding='utf-8',xml_declaration=True)


def digest(data):return hashlib.sha256(data).hexdigest()


class Profile:
    def __init__(self,root,state,busy=lambda:False,gate=None):
        self.root=Path(root).resolve();self.state=Path(state).resolve();self.state.mkdir(parents=True,exist_ok=True)
        self.busy=busy;self.lock=threading.RLock();self.cache={};self.gate=gate

    def path(self,name):
        parts=PurePosixPath(name).parts
        if not parts or PurePosixPath(name).is_absolute() or any(p in ('.','..') or ':' in p or '\\' in p or p.endswith(('.', ' ')) for p in parts):raise ValueError('Ungültiger Dateipfad')
        path=self.root.joinpath(*parts).resolve()
        if not path.is_relative_to(self.root) or path==self.root or path.is_relative_to(self.state):raise ValueError('Dateipfad außerhalb des Profils')
        return path

    def read(self,name):
        path=self.path(name);before=path.stat()
        if before.st_size>MAX_FILE:raise ValueError('Datei über 256 MB: '+name)
        data=path.read_bytes();after=path.stat()
        if (before.st_size,before.st_mtime_ns,before.st_ino,before.st_ctime_ns)!=(after.st_size,after.st_mtime_ns,after.st_ino,after.st_ctime_ns):raise RuntimeError('Datei wird gerade verändert')
        return portable(data,name)

    def revision(self,name):
        try:return digest(self.read(name))
        except FileNotFoundError:return None

    def manifest(self):
        with self.lock:
            if self.busy():raise RuntimeError('Traktor läuft; Abgleich wartet bis zum Schließen')
            result={}
            for path in self.root.rglob('*'):
                if self.busy():raise RuntimeError('Traktor wurde gestartet; Abgleich pausiert')
                if not path.is_file() or path.is_symlink() or path.is_relative_to(self.state):continue
                name=path.relative_to(self.root).as_posix()
                if '.sync-conflict-' in name or name.endswith(('.tmp','.syncing','.importing')):continue
                stat=path.stat();stamp=(stat.st_size,stat.st_mtime_ns,stat.st_ino,stat.st_ctime_ns)
                if stat.st_size>MAX_FILE:continue
                cached=self.cache.get(name)
                if not cached or cached[0]!=stamp:
                    cached=(stamp,self.revision(name));self.cache[name]=cached
                result[name]=cached[1]
            return result

    def write(self,name,data,expected):
        with self.lock, (hold_profile(self.gate) if self.gate else nullcontext()):
            if self.busy():raise RuntimeError('Traktor läuft; Datei wurde nicht ersetzt')
            path=self.path(name)
            if len(data)>MAX_FILE:raise ValueError('Datei zu groß')
            if self.revision(name)!=expected:raise RuntimeError('Datei wurde inzwischen geändert')
            previous=path.read_bytes() if path.exists() else None
            contents=install_bytes(data,name,previous)
            path.parent.mkdir(parents=True,exist_ok=True)
            temporary=path.with_name(path.name+'.'+uuid.uuid4().hex+'.syncing')
            try:
                with temporary.open('xb') as out:out.write(contents);out.flush();os.fsync(out.fileno())
                if self.busy() or self.revision(name)!=expected:raise RuntimeError('Profil inzwischen geöffnet oder geändert')
                if previous is not None:
                    backup=self.state/'backups';backup.mkdir(exist_ok=True)
                    (backup/(digest(name.encode())[:16]+'-'+uuid.uuid4().hex+path.suffix)).write_bytes(previous)
                os.replace(temporary,path);self.cache.pop(name,None)
            finally:temporary.unlink(missing_ok=True)


class Endpoint:
    def __init__(self,profile,key,node=None,name=None):
        if len(key)!=32:raise ValueError('Kopplungsschlüssel muss 32 Bytes lang sein')
        self.profile=profile;self.key=key;self.node=node or uuid.uuid4().hex;self.name=name or socket.gethostname()
        self.group=digest(key)[:16];self.nonces={};self.lock=threading.Lock();self.server=None

    def signature(self,method,path,nonce,body):return hmac.new(self.key,('\n'.join((method,path,nonce,digest(body)))).encode(),hashlib.sha256).hexdigest()

    def start(self,host='0.0.0.0',port=42518):
        endpoint=self
        class Handler(BaseHTTPRequestHandler):
            def log_message(self,*args):pass
            def setup(self):super().setup();self.connection.settimeout(10)
            def do_GET(self):self.handle_request()
            def do_PUT(self):self.handle_request()
            def handle_request(self):
                nonce=self.headers.get('X-Cratebase-Nonce','');body=b''
                try:
                    length=int(self.headers.get('Content-Length','0'))
                    if not 0<=length<=MAX_FILE:raise ValueError('Ungültige Größe')
                    timestamp=float(nonce.split(':')[0])
                    if abs(time.time()-timestamp)>60:raise ValueError('Abgelaufene Anfrage')
                    declared=self.headers.get('X-Cratebase-Body','')
                    signed_path=self.path+'\n'+self.headers.get('X-Cratebase-Expected','')
                    expected=hmac.new(endpoint.key,('\n'.join((self.command,signed_path,nonce,declared))).encode(),hashlib.sha256).hexdigest()
                    if not hmac.compare_digest(expected,self.headers.get('X-Cratebase-Auth','')):raise ValueError('Kopplungsschlüssel stimmt nicht überein')
                    with endpoint.lock:
                        endpoint.nonces={n:t for n,t in endpoint.nonces.items() if time.time()-t<60}
                        if nonce in endpoint.nonces or len(endpoint.nonces)>4096:raise ValueError('Wiederholte Anfrage')
                        endpoint.nonces[nonce]=time.time()
                    body=self.rfile.read(length)
                    if len(body)!=length or digest(body)!=declared:raise ValueError('Unvollständige oder veränderte Übertragung')
                except (ValueError,TimeoutError):self.reply(403,b'{"error":"Authentifizierung fehlgeschlagen"}',nonce);return
                try:
                    parsed=urlsplit(self.path)
                    if parsed.path=='/status':value={'node':endpoint.node,'name':endpoint.name,'busy':bool(endpoint.profile.busy())}
                    elif parsed.path=='/manifest':value={'node':endpoint.node,'files':endpoint.profile.manifest()}
                    elif parsed.path=='/file':
                        name=parse_qs(parsed.query,strict_parsing=True)['path'][0]
                        if self.command=='PUT':
                            expected=self.headers.get('X-Cratebase-Expected') or None
                            endpoint.profile.write(name,body,expected);value={'ok':True}
                        else:self.reply(200,endpoint.profile.read(name),nonce);return
                    else:raise ValueError('Unbekannte Anfrage')
                    self.reply(200,json.dumps(value).encode(),nonce)
                except (OSError,ValueError,RuntimeError,KeyError,ET.ParseError) as error:
                    self.reply(409,json.dumps({'error':str(error)}).encode(),nonce)
            def reply(self,status,body,nonce):
                self.send_response(status);self.send_header('Content-Length',str(len(body)))
                self.send_header('X-Cratebase-Response',endpoint.signature(str(status),'response',nonce,body));self.end_headers()
                try:self.wfile.write(body)
                except OSError:pass
        class BoundedServer(ThreadingHTTPServer):
            def __init__(self,*args):self.slots=threading.BoundedSemaphore(8);super().__init__(*args)
            def process_request(self,request,address):
                if not self.slots.acquire(blocking=False):request.close();return
                try:super().process_request(request,address)
                except Exception:self.slots.release();raise
            def process_request_thread(self,*args):
                try:super().process_request_thread(*args)
                finally:self.slots.release()
        self.server=BoundedServer((host,port),Handler);self.server.daemon_threads=True
        threading.Thread(target=self.server.serve_forever,daemon=True).start()
        return self.server.server_address[1]

    def close(self):
        if self.server:self.server.shutdown();self.server.server_close();self.server=None

    def request(self,host,port,path,method='GET',body=b'',expected=None):
        nonce=str(time.time())+':'+secrets.token_hex(16)
        headers={'X-Cratebase-Nonce':nonce,'X-Cratebase-Body':digest(body),'X-Cratebase-Auth':self.signature(method,path+'\n'+(expected or ''),nonce,body)}
        if expected:headers['X-Cratebase-Expected']=expected
        connection=http.client.HTTPConnection(host,port,timeout=30)
        try:
            connection.request(method,path,body=body,headers=headers);response=connection.getresponse()
            size=int(response.getheader('Content-Length','0'))
            if not 0<=size<=MAX_FILE:raise ValueError('Ungültige Antwortgröße')
            data=response.read(size)
            if len(data)!=size or not hmac.compare_digest(self.signature(str(response.status),'response',nonce,data),response.getheader('X-Cratebase-Response','')):raise ValueError('Antwort nicht authentifiziert')
            if response.status!=200:raise RuntimeError(json.loads(data).get('error','Abgleich fehlgeschlagen'))
            return data
        finally:connection.close()

    def sync(self,host,port):
        if self.profile.busy():return {'busy':True,'copied':0,'conflicts':[]}
        remote=json.loads(self.request(host,port,'/manifest'));node=remote['node']
        if not isinstance(node,str) or len(node)!=32 or any(c not in '0123456789abcdef' for c in node):raise ValueError('Ungültige Gerätekennung')
        if node==self.node:return {'copied':0,'conflicts':[]}
        state_path=self.profile.state/('peer-'+node+'.json')
        baseline=json.loads(state_path.read_text(encoding='utf-8')) if state_path.exists() else {}
        own=self.profile.manifest();other=remote['files'];conflicts=[];copied=0
        for name in sorted(set(own)|set(other)|set(baseline)):
            self.profile.path(name)
            left,right,base=own.get(name),other.get(name),baseline.get(name)
            if left==right:
                if left:baseline[name]=left
                continue
            if left and right and left!=base and right!=base:
                conflicts.append({'path':name,'local':left,'remote':right,'base':base});continue
            # Missing files with a known baseline are retained as conflicts,
            # rather than treating an offline drive or deletion as authority.
            if base and (not left or not right):
                conflicts.append({'path':name,'local':left,'remote':right,'base':base});continue
            if right and (left==base or not left):
                data=self.request(host,port,'/file?path='+quote(name,safe=''))
                if digest(data)!=right:raise RuntimeError('Quelldatei während des Abgleichs geändert')
                self.profile.write(name,data,left);baseline[name]=right;copied+=1
            elif left and (right==base or not right):
                data=self.profile.read(name)
                if digest(data)!=left:raise RuntimeError('Lokale Datei während des Abgleichs geändert')
                self.request(host,port,'/file?path='+quote(name,safe=''),'PUT',data,right);baseline[name]=left;copied+=1
        atomic_write(state_path,baseline)
        atomic_write(self.profile.state/('conflicts-'+node+'.json'),conflicts)
        return {'copied':copied,'conflicts':conflicts,'node':node}


class Discovery:
    def __init__(self,endpoint,port,tcp_port):
        self.endpoint=endpoint;self.port=port;self.tcp_port=tcp_port;self.peers={};self.stop=threading.Event();self.lock=threading.Lock()
        self.socket=socket.socket(socket.AF_INET,socket.SOCK_DGRAM);self.socket.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1);self.socket.setsockopt(socket.SOL_SOCKET,socket.SO_BROADCAST,1)
        self.socket.bind(('',port));self.socket.settimeout(1)
        threading.Thread(target=self.run,daemon=True).start()
    def run(self):
        last=0
        while not self.stop.is_set():
            if time.monotonic()-last>5:
                packet={'cratebase':1,'group':self.endpoint.group,'node':self.endpoint.node,'name':self.endpoint.name,'port':self.tcp_port}
                try:self.socket.sendto(json.dumps(packet).encode(),('255.255.255.255',self.port))
                except OSError:pass
                last=time.monotonic()
            try:
                data,address=self.socket.recvfrom(4096);packet=json.loads(data)
                if not isinstance(packet,dict):continue
                node=packet.get('node')
                if not isinstance(node,str) or len(node)!=32 or any(c not in '0123456789abcdef' for c in node):continue
                if packet.get('cratebase')==1 and packet.get('group')==self.endpoint.group and packet.get('node')!=self.endpoint.node and 1<=int(packet.get('port',0))<=65535:
                    with self.lock:
                        self.peers={n:p for n,p in self.peers.items() if time.time()-p['seen']<30}
                        if len(self.peers)<256:self.peers[node]={'host':address[0],'port':int(packet['port']),'name':str(packet.get('name','PC'))[:100],'seen':time.time()}
            except (OSError,ValueError,TypeError,KeyError):pass
    def snapshot(self):
        with self.lock:return list(self.peers.values())
    def close(self):self.stop.set();self.socket.close()
