// Loaded before the lab process resumes. Redirect only Traktor settings/data, not activation/licensing.
// Registry values and user-data paths are redirected to private copies, not globally changed.
const config=LAB_CONFIG;
const redirected=new Set();
const handlePaths=new Map();
const hookAddresses=new Set();
function emit(type,detail){send({type,time:Date.now(),...detail});}
function exported(dll,name){try{return Process.getModuleByName(dll).getExportByName(name);}catch{return null;}}
function safeText(pointer,wide){try{return pointer.isNull()?null:wide?pointer.readUtf16String():pointer.readUtf8String();}catch{return null;}}
function rewritePath(path){
    if(!path)return path;
    let value=path.replace(/\//g,'\\');const prefix=value.startsWith('\\\\?\\')?'\\\\?\\':value.startsWith('\\??\\')?'\\??\\':'';if(prefix)value=value.substring(4);
    const normalized=value.toLowerCase();
    // The edition profile lives beside the normal version directories. Keep
    // its own paths out of the broader Native Instruments redirection below.
    if(config.profile_directory){
        const target=config.profile_directory.toLowerCase();
        if(normalized===target||normalized.startsWith(target+'\\'))return path;
        for(const original of [config.original_documents_ni+'\\Traktor 4.5.1',config.legacy_profile_directory]){
            if(!original)continue;
            const match=original.toLowerCase();
            if(normalized===match||normalized.startsWith(match+'\\'))return prefix+config.profile_directory+value.substring(original.length);
        }
    }
    for(const [original,replacement] of [[config.original_documents_ni,config.lab_documents_ni],[config.original_local_ni,config.lab_local_ni],[config.original_roaming_ni,config.lab_roaming_ni]]){
        const match=original.toLowerCase();
        if(normalized===match||normalized.startsWith(match+'\\'))return prefix+replacement+value.substring(original.length);
    }
    return path;
}
function fileHook(name,wide,indices){
    const address=exported('kernel32.dll',name);if(!address||hookAddresses.has(address.toString()))return;
    hookAddresses.add(address.toString());
    Interceptor.attach(address,{onEnter(args){
        this.keep=[];
        for(const index of indices){const original=safeText(args[index],wide);const replacement=rewritePath(original);if(original&&replacement!==original){const memory=wide?Memory.allocUtf16String(replacement):Memory.allocUtf8String(replacement);this.keep.push(memory);args[index]=memory;if(!redirected.has(original)){redirected.add(original);emit('file_redirect',{api:name,original,replacement});}}}
    }});
}
for(const name of ['CreateFileW','GetFileAttributesW','GetFileAttributesExW','FindFirstFileW','FindFirstFileExW','CreateDirectoryW','DeleteFileW','RemoveDirectoryW','SetFileAttributesW'])fileHook(name,true,[0]);
for(const name of ['CreateFileA','GetFileAttributesA','DeleteFileA'])fileHook(name,false,[0]);
for(const name of ['MoveFileW','MoveFileExW','CopyFileW','CopyFileExW','CreateDirectoryExW'])fileHook(name,true,[0,1]);
fileHook('MoveFileExA',false,[0,1]);
fileHook('ReplaceFileW',true,[0,1,2]);
// C runtime filesystem calls can reach NtCreateFile directly, bypassing Kernel32.
// Rewrite a private OBJECT_ATTRIBUTES copy, preserving all flags and root handles.
for(const [name,index] of (config.native_file_hooks?[['NtCreateFile',2],['NtOpenFile',2],['NtQueryAttributesFile',0],['NtQueryFullAttributesFile',0]]:[])){
    const address=exported('ntdll.dll',name);if(!address)continue;
    Interceptor.attach(address,{onEnter(args){
        try{
            const attributes=args[index];if(attributes.isNull())return;
            const unicode=attributes.add(16).readPointer();if(unicode.isNull())return;
            const length=unicode.readU16(),buffer=unicode.add(8).readPointer();
            const original=buffer.readUtf16String(length/2),replacement=rewritePath(original);
            if(replacement===original)return;
            const data=Memory.allocUtf16String(replacement),text=Memory.alloc(16),copy=Memory.alloc(48);
            text.writeU16(replacement.length*2);text.add(2).writeU16((replacement.length+1)*2);text.add(8).writePointer(data);
            copy.writeByteArray(attributes.readByteArray(48));copy.add(16).writePointer(text);
            this.keep=[data,text,copy];args[index]=copy;
            if(!redirected.has(original)){redirected.add(original);emit('file_redirect',{api:name,original,replacement});}
        }catch(error){emit('redirect_error',{api:name,message:String(error)});}
    }});
}
function parentName(handle){
    const low=handle.toUInt32();if(low===0x80000000)return 'HKCR';if(low===0x80000001)return 'HKCU';if(low===0x80000002)return 'HKLM';return handlePaths.get(handle.toString())||null;
}
function registryHook(name,wide,resultIndex){
    const address=exported('advapi32.dll',name);if(!address)return;
    Interceptor.attach(address,{onEnter(args){
        this.output=args[resultIndex];const parent=parentName(args[0]);const child=safeText(args[1],wide);this.path=parent&&child?parent+'\\'+child:null;
        if(!this.path)return;
        const lower=this.path.toLowerCase();const prefix='hkcu\\software\\native instruments\\traktor pro 4';
        if(lower==='hkcu\\software\\classes\\traktor'||lower.startsWith('hkcu\\software\\classes\\traktor\\')||lower==='hkcr\\traktor'||lower.startsWith('hkcr\\traktor\\')){
            const relative=lower.startsWith('hkcr\\')?this.path.substring(5):this.path.substring('HKCU\\Software\\Classes\\'.length);
            const replacement='Software\\Cratebase\\TraktorLiveStemsLab\\Classes\\'+relative;
            this.memory=wide?Memory.allocUtf16String(replacement):Memory.allocUtf8String(replacement);args[0]=ptr('0x80000001');args[1]=this.memory;this.path='HKCU\\'+replacement;emit('registry_redirect',{api:name,path:this.path});return;
        }
        if(lower===prefix||lower.startsWith(prefix+'\\')||lower.startsWith(prefix+' 4.5.1')){
            const relative=this.path.substring('HKCU\\Software\\Native Instruments\\'.length);
            const replacement='Software\\Cratebase\\TraktorLiveStemsLab\\'+relative;
            this.memory=wide?Memory.allocUtf16String(replacement):Memory.allocUtf8String(replacement);args[0]=ptr('0x80000001');args[1]=this.memory;this.path='HKCU\\'+replacement;emit('registry_redirect',{api:name,path:this.path});
        }
    },onLeave(result){if(result.toInt32()===0&&this.path){try{handlePaths.set(this.output.readPointer().toString(),this.path);}catch{}}}});
}
for(const wide of [true,false]){
    registryHook('RegOpenKeyEx'+(wide?'W':'A'),wide,4);
    registryHook('RegCreateKeyEx'+(wide?'W':'A'),wide,7);
    registryHook('RegOpenKey'+(wide?'W':'A'),wide,2);
}
const close=exported('advapi32.dll','RegCloseKey');if(close)Interceptor.attach(close,{onEnter(args){handlePaths.delete(args[0].toString());}});
// Informational module snapshot; no authentication or DRM hooks.
emit('sandbox_ready',{pid:Process.id,modules:Process.enumerateModules().map(m=>({name:m.name,base:m.base.toString(),size:m.size}))});

