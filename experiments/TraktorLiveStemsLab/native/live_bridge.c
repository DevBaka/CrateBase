// Traktor 4.5.1 lab source bridge. Original executable/resources remain untouched.
// Control paths may lock/allocate; read_cached does neither and never invokes Python/JS/GPU.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <string.h>
#include <math.h>

#define EXPORT __declspec(dllexport)
#define BLOCK 1024
#define MAX_READERS 256
#define MAX_CACHES 16
#define PHASES 256
#define TAPS 32
typedef int64_t (*ReadFn)(void*,float**);
typedef void* (*DestroyFn)(void*,unsigned);
typedef struct {
    char magic[8];uint32_t version,sample_rate,block_frames,channels;
    uint64_t total_frames,block_count,data_offset;
    volatile LONG ready_blocks,requested_block,error_code,generation;
    uint64_t original_offset;volatile LONG original_ready;
} CacheHeader;
typedef struct {
    HANDLE file,map_handle;unsigned char* map;CacheHeader* header;
    volatile LONG readers;wchar_t path[2048];uint64_t last_used;
} Cache;
typedef struct {
    void* object;void** original_table;ReadFn original;DestroyFn destroy;
    void* table[19];unsigned stem,cache_index,source_rate,mix_fallback;
    double ratio;float* kernel;int last_mode,fade_remaining,fade_total;
    uint64_t calls,replaced,fallback;
} Reader;
static Cache caches[MAX_CACHES];static Reader readers[MAX_READERS];
static volatile LONG reader_count,enabled=1;static SRWLOCK control_lock=SRWLOCK_INIT;
#define MAX_DOWNSTREAM 256
typedef uint64_t (*CacheReadFn)(void*,unsigned,float*,float*,unsigned*);
typedef struct {void* object;Reader sampler;uint64_t calls,replaced,frames;int mode,fade;float peak;double energy;int64_t position;} Downstream;
static Downstream downstream[MAX_DOWNSTREAM];static volatile LONG downstream_count;
static CacheReadFn cache_read_original;

static Reader* find_reader(void* object){for(LONG i=0;i<reader_count;i++)if(readers[i].object==object)return &readers[i];return 0;}
static LONG load_long(volatile LONG* value){return InterlockedCompareExchange(value,0,0);}
static float cache_value(Cache* cache,unsigned stem,unsigned channel,int64_t frame,int original){
    CacheHeader* h=cache->header;if(frame<0||(uint64_t)frame>=h->total_frames)return 0;
    if(original)return ((float*)(cache->map+h->original_offset))[channel*h->total_frames+frame];
    return ((float*)(cache->map+h->data_offset))[((uint64_t)frame/BLOCK)*8*BLOCK+((stem-1)*2+channel)*BLOCK+((uint64_t)frame%BLOCK)];
}
static float sample(Reader* reader,Cache* cache,unsigned channel,double position,int original){
    int64_t frame=(int64_t)floor(position);
    if(!reader->kernel)return cache_value(cache,reader->stem,channel,frame,original);
    unsigned phase=(unsigned)((position-frame)*PHASES);if(phase>=PHASES)phase=PHASES-1;
    const float* kernel=reader->kernel+phase*TAPS;float value=0;
    for(int tap=0;tap<TAPS;tap++)value+=kernel[tap]*cache_value(cache,reader->stem,channel,frame+tap-(TAPS/2-1),original);
    return value;
}
static int gpu_ready(Reader* reader,Cache* cache,int64_t position){
    CacheHeader* h=cache->header;int margin=reader->kernel?TAPS/2:0;
    int64_t first=(int64_t)floor(position*reader->ratio)-margin;
    int64_t last=(int64_t)ceil((position+BLOCK-1)*reader->ratio)+margin;
    if(first<0)first=0;if(last>=(int64_t)h->total_frames)last=h->total_frames-1;
    if(first>last)return 0;volatile LONG* ready=(volatile LONG*)(cache->map+4096);
    for(uint64_t b=(uint64_t)first/BLOCK;b<=(uint64_t)last/BLOCK;b++)if(!load_long(&ready[b]))return 0;
    return 1;
}
static int64_t delegate(Reader* reader,void* object,float** outputs){
    int64_t frames=reader->original(object,outputs);
    if(reader->mix_fallback&&reader->stem&&frames>0&&frames<=BLOCK)
        for(int ch=0;ch<2;ch++)for(int64_t i=0;i<frames;i++)outputs[ch][i]*=.25f;
    return frames;
}
static int64_t read_cached(void* object,float** outputs){
    Reader* reader=find_reader(object);if(!reader)return 0;
    Cache* cache=&caches[reader->cache_index];CacheHeader* h=cache->header;
    int64_t position=*(int64_t*)((unsigned char*)object+0xe0);reader->calls++;
    if(!h||position<0||!outputs||!outputs[0]||!outputs[1])return delegate(reader,object,outputs);
    if(position*reader->ratio>=(double)(h->block_count*BLOCK))return delegate(reader,object,outputs);
    int original_ready=h->original_offset&&load_long(&h->original_ready);
    int available=reader->stem&&gpu_ready(reader,cache,position);
    int mode=available&&load_long(&enabled);
    if(!mode&&!original_ready){reader->fallback++;reader->last_mode=0;InterlockedExchange(&h->requested_block,(LONG)(position*reader->ratio/BLOCK));return delegate(reader,object,outputs);}
    if(reader->last_mode>=0&&reader->last_mode!=mode&&available&&original_ready){reader->fade_total=(int)(reader->source_rate*.008);reader->fade_remaining=reader->fade_total;}
    reader->last_mode=mode;
    if(mode)reader->replaced++;else{reader->fallback++;InterlockedExchange(&h->requested_block,(LONG)(position*reader->ratio/BLOCK));}
    float gain=reader->stem?.25f:1.f;
    for(int i=0;i<BLOCK;i++){
        double source=(position+i)*reader->ratio;
        float blend=reader->fade_remaining&&reader->fade_total?1.f-(float)reader->fade_remaining/reader->fade_total:1.f;
        for(unsigned ch=0;ch<2;ch++){
            float value=mode?sample(reader,cache,ch,source,0):sample(reader,cache,ch,source,1)*gain;
            if(reader->fade_remaining&&available&&original_ready){float previous=mode?sample(reader,cache,ch,source,1)*gain:sample(reader,cache,ch,source,0);value=previous+(value-previous)*blend;}
            outputs[ch][i]=value;
        }
        if(reader->fade_remaining)reader->fade_remaining--;
    }
    *(int64_t*)((unsigned char*)object+0xe0)=position+BLOCK;
    *(unsigned char*)((unsigned char*)object+0xd0)=1;*(uint32_t*)((unsigned char*)object+0xd4)=0;
    return BLOCK;
}
static void* destroy_reader(void* object,unsigned flags){
    Reader* reader=find_reader(object);if(!reader)return object;
    DestroyFn destroy=reader->destroy;Cache* cache=&caches[reader->cache_index];
    *(void***)object=reader->original_table;
    InterlockedExchangePointer((void* volatile*)&reader->object,0);InterlockedDecrement(&cache->readers);
    // Reader slots are reused only by the control path. Retain kernels until reused.
    return destroy(object,flags);
}
static void close_cache(Cache* cache){
    if(cache->map)UnmapViewOfFile(cache->map);if(cache->map_handle)CloseHandle(cache->map_handle);
    if(cache->file&&cache->file!=INVALID_HANDLE_VALUE)CloseHandle(cache->file);memset(cache,0,sizeof(*cache));
}
static int validate(CacheHeader* h,uint64_t bytes){
    if(bytes<76||memcmp(h->magic,"CBSTEM01",8)||h->version!=1||h->sample_rate<8000||h->sample_rate>192000||h->block_frames!=BLOCK||h->channels!=8)return 0;
    if(!h->total_frames||h->total_frames>UINT64_C(192000)*3600||h->block_count!=(h->total_frames+BLOCK-1)/BLOCK)return 0;
    if(h->data_offset<4096+h->block_count*4||h->data_offset+h->block_count*8*BLOCK*4>bytes)return 0;
    if(h->original_offset&&(h->original_offset<h->data_offset+h->block_count*8*BLOCK*4||h->original_offset+h->total_frames*8>bytes))return 0;
    return 1;
}
static int add_cache(const wchar_t* path,uint64_t frames,unsigned rate,int create){
    if(!path||lstrlenW(path)>=2048)return -1;AcquireSRWLockExclusive(&control_lock);
    int slot=-1;
    for(int i=0;i<MAX_CACHES;i++)if(caches[i].map&&!lstrcmpiW(caches[i].path,path)){caches[i].last_used=GetTickCount64();ReleaseSRWLockExclusive(&control_lock);return i;}
    for(int i=0;i<MAX_CACHES;i++)if(!caches[i].map){slot=i;break;}
    if(slot<0)for(int i=0;i<MAX_CACHES;i++)if(!load_long(&caches[i].readers)&&(slot<0||caches[i].last_used<caches[slot].last_used))slot=i;
    if(slot<0){ReleaseSRWLockExclusive(&control_lock);return -2;}
    Cache* cache=&caches[slot];close_cache(cache);
    cache->file=CreateFileW(path,GENERIC_READ|GENERIC_WRITE,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,0,create?OPEN_ALWAYS:OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,0);
    if(cache->file==INVALID_HANDLE_VALUE){int error=-(int)GetLastError()-1000;close_cache(cache);ReleaseSRWLockExclusive(&control_lock);return error;}
    LARGE_INTEGER size;GetFileSizeEx(cache->file,&size);
    if(create&&size.QuadPart==0){
        if(!frames||frames>UINT64_C(192000)*3600||rate<8000||rate>192000){close_cache(cache);ReleaseSRWLockExclusive(&control_lock);return -3;}
        CacheHeader h={0};memcpy(h.magic,"CBSTEM01",8);h.version=1;h.sample_rate=rate;h.block_frames=BLOCK;h.channels=8;h.total_frames=frames;h.block_count=(frames+BLOCK-1)/BLOCK;h.data_offset=((4096+h.block_count*4+4095)/4096)*4096;h.original_offset=h.data_offset+h.block_count*8*BLOCK*4;h.requested_block=-1;h.generation=1;
        size.QuadPart=h.original_offset+frames*8;DWORD ignored,written;
        // Mark only our private cache sparse before extending its logical size.
        DeviceIoControl(cache->file,0x900c4,0,0,0,0,&ignored,0);
        SetFilePointerEx(cache->file,size,0,FILE_BEGIN);SetEndOfFile(cache->file);LARGE_INTEGER zero;zero.QuadPart=0;SetFilePointerEx(cache->file,zero,0,FILE_BEGIN);WriteFile(cache->file,&h,sizeof(h),&written,0);
    }
    cache->map_handle=CreateFileMappingW(cache->file,0,PAGE_READWRITE,0,0,0);
    if(cache->map_handle)cache->map=(unsigned char*)MapViewOfFile(cache->map_handle,FILE_MAP_READ|FILE_MAP_WRITE,0,0,0);
    if(!cache->map||!validate((CacheHeader*)cache->map,(uint64_t)size.QuadPart)){close_cache(cache);ReleaseSRWLockExclusive(&control_lock);return -4;}
    cache->header=(CacheHeader*)cache->map;lstrcpyW(cache->path,path);cache->last_used=GetTickCount64();ReleaseSRWLockExclusive(&control_lock);return slot;
}
EXPORT int LabAddCache(const wchar_t* path){return add_cache(path,0,0,0);}
EXPORT int LabCreateCache(const wchar_t* path,uint64_t frames,unsigned rate){return add_cache(path,frames,rate,1);}
EXPORT int LabConfigure(const wchar_t* path){int result=LabAddCache(path);return result<0?result:0;}
EXPORT int LabRegisterSource(void* object,unsigned stem,unsigned cache_index,unsigned source_rate,unsigned mix_fallback){
    if(!object||stem>4||cache_index>=MAX_CACHES||!caches[cache_index].map||source_rate<8000||source_rate>192000)return -1;
    AcquireSRWLockExclusive(&control_lock);Reader* reader=find_reader(object);
    if(reader&&*(void***)object==reader->table+1){ReleaseSRWLockExclusive(&control_lock);return 0;}
    if(!reader){for(LONG i=0;i<reader_count;i++)if(!readers[i].object){reader=&readers[i];break;}}
    if(!reader){LONG index=reader_count;if(index>=MAX_READERS){ReleaseSRWLockExclusive(&control_lock);return -2;}reader=&readers[index];InterlockedExchange(&reader_count,index+1);}
    if(reader->kernel)HeapFree(GetProcessHeap(),0,reader->kernel);memset(reader,0,sizeof(*reader));
    reader->stem=stem;reader->cache_index=cache_index;reader->source_rate=source_rate;reader->mix_fallback=mix_fallback;reader->ratio=(double)caches[cache_index].header->sample_rate/source_rate;reader->last_mode=-1;
    reader->original_table=*(void***)object;reader->original=(ReadFn)reader->original_table[17];reader->destroy=(DestroyFn)reader->original_table[0];
    if(source_rate!=caches[cache_index].header->sample_rate){
        reader->kernel=(float*)HeapAlloc(GetProcessHeap(),0,PHASES*TAPS*sizeof(float));if(!reader->kernel){ReleaseSRWLockExclusive(&control_lock);return -3;}
        double cutoff=reader->ratio>1?1/reader->ratio:1;
        for(int phase=0;phase<PHASES;phase++){double sum=0;for(int tap=0;tap<TAPS;tap++){
            double x=tap-(TAPS/2-1)-(double)phase/PHASES;double angle=3.141592653589793*x*cutoff;
            double sinc=fabs(angle)<1e-12?1:sin(angle)/angle;double window=.42-.5*cos(2*3.141592653589793*tap/(TAPS-1))+.08*cos(4*3.141592653589793*tap/(TAPS-1));
            reader->kernel[phase*TAPS+tap]=(float)(sinc*window*cutoff);sum+=reader->kernel[phase*TAPS+tap];
        }for(int tap=0;tap<TAPS;tap++)reader->kernel[phase*TAPS+tap]/=(float)sum;}
    }
    memcpy(reader->table,reader->original_table-1,19*sizeof(void*));reader->table[1]=(void*)destroy_reader;reader->table[18]=(void*)read_cached;
    InterlockedIncrement(&caches[cache_index].readers);reader->object=object;InterlockedExchangePointer((void* volatile*)object,reader->table+1);ReleaseSRWLockExclusive(&control_lock);return 0;
}
EXPORT int LabRegister(void* object,unsigned stem){return LabRegisterSource(object,stem,0,caches[0].header?caches[0].header->sample_rate:0,0);}
EXPORT uint64_t LabStatistics(unsigned index,unsigned field){if(index>=(unsigned)reader_count)return 0;Reader* r=&readers[index];switch(field){case 0:return(uintptr_t)r->object;case 1:return r->stem;case 2:return r->calls;case 3:return r->replaced;case 4:return r->fallback;case 5:return r->cache_index;default:return 0;}}
EXPORT unsigned LabReaderCount(void){return reader_count;}
EXPORT void LabSetEnabled(int value){InterlockedExchange(&enabled,value?1:0);}
EXPORT void LabSetCacheReadOriginal(void* function){cache_read_original=(CacheReadFn)function;}
EXPORT int LabRegisterDownstream(void* object,void* source){
    if(!object)return -1;AcquireSRWLockExclusive(&control_lock);Reader* reader=find_reader(source);
    if(!reader||!reader->stem){ReleaseSRWLockExclusive(&control_lock);return 1;}
    Downstream* slot=0;for(LONG i=0;i<downstream_count;i++)if(downstream[i].object==object){slot=&downstream[i];break;}
    if(!slot)for(LONG i=0;i<downstream_count;i++)if(!downstream[i].object){slot=&downstream[i];break;}
    if(!slot){if(downstream_count>=MAX_DOWNSTREAM){ReleaseSRWLockExclusive(&control_lock);return -2;}slot=&downstream[downstream_count++];}
    if(slot->object)InterlockedDecrement(&caches[slot->sampler.cache_index].readers);
    if(slot->sampler.kernel)HeapFree(GetProcessHeap(),0,slot->sampler.kernel);
    memset(slot,0,sizeof(*slot));slot->sampler=*reader;slot->sampler.kernel=0;slot->mode=-1;
    if(reader->kernel){slot->sampler.kernel=HeapAlloc(GetProcessHeap(),0,PHASES*TAPS*sizeof(float));if(!slot->sampler.kernel){ReleaseSRWLockExclusive(&control_lock);return -3;}memcpy(slot->sampler.kernel,reader->kernel,PHASES*TAPS*sizeof(float));}
    InterlockedIncrement(&caches[reader->cache_index].readers);InterlockedExchangePointer((void* volatile*)&slot->object,object);ReleaseSRWLockExclusive(&control_lock);return 0;
}
EXPORT void LabUnregisterDownstream(void* object){
    AcquireSRWLockExclusive(&control_lock);for(LONG i=0;i<downstream_count;i++)if(downstream[i].object==object){InterlockedExchangePointer((void* volatile*)&downstream[i].object,0);InterlockedDecrement(&caches[downstream[i].sampler.cache_index].readers);break;}ReleaseSRWLockExclusive(&control_lock);
}
EXPORT uint64_t LabReadDownstream(void* object,unsigned frames,float* left,float* right,unsigned* actual){
    Downstream* slot=0;for(LONG i=0;i<downstream_count;i++)if(downstream[i].object==object){slot=&downstream[i];break;}
    if(!cache_read_original)return 0;
    if(!slot||!left||!right||frames>131072)return cache_read_original(object,frames,left,right,actual);
    unsigned char* primary=*(unsigned char**)((unsigned char*)object+0x10);
    if(!primary)return cache_read_original(object,frames,left,right,actual);
    int64_t position=*(uint32_t*)(primary+0xac);int direction=*(unsigned char*)(primary+0xb4)?-1:1;
    uint64_t result=cache_read_original(object,frames,left,right,actual);slot->calls++;slot->position=position;
    int64_t after=*(uint32_t*)(primary+0xac);
    // A failed native try-lock leaves position unchanged: preserve that native result.
    if(frames&&after==position)return result;
    Reader* reader=&slot->sampler;Cache* cache=&caches[reader->cache_index];CacheHeader* h=cache->header;
    int64_t last=position+direction*(int64_t)(frames?frames-1:0);int64_t lo=position<last?position:last,hi=position>last?position:last;
    int64_t first=(int64_t)floor(lo*reader->ratio)-(reader->kernel?TAPS/2:0),end=(int64_t)ceil(hi*reader->ratio)+(reader->kernel?TAPS/2:0);
    if(first<0)first=0;if(end>=(int64_t)h->total_frames)end=h->total_frames-1;
    int ready=end>=first;volatile LONG* bits=(volatile LONG*)(cache->map+4096);
    for(uint64_t b=(uint64_t)first/BLOCK;ready&&b<=(uint64_t)end/BLOCK;b++)if(!load_long(&bits[b]))ready=0;
    int mode=ready&&load_long(&enabled),original=h->original_offset&&load_long(&h->original_ready);
    if(!mode)InterlockedExchange(&h->requested_block,(LONG)(position*reader->ratio/BLOCK));
    if(!mode&&!original){InterlockedExchange(&h->requested_block,(LONG)(position*reader->ratio/BLOCK));return result;}
    if(slot->mode>=0&&mode!=slot->mode)slot->fade=(int)(reader->source_rate*.008);slot->mode=mode;
    int fade_total=(int)(reader->source_rate*.008);unsigned available=actual?*actual:frames;if(available>frames)available=frames;
    for(unsigned i=0;i<available;i++){
        double source=(position+direction*(int64_t)i)*reader->ratio;
        float blend=slot->fade?1.f-(float)slot->fade/fade_total:1.f;
        for(unsigned ch=0;ch<2;ch++){
            float* output=ch?right:left;
            float value=sample(reader,cache,ch,source,!mode)*(mode?1.f:.25f);
            if(slot->fade){
                float previous=ready&&original?sample(reader,cache,ch,source,mode)*(mode?.25f:1.f):output[i];
                value=previous+(value-previous)*blend;
            }
            output[i]=value;slot->energy+=(double)value*value;if(fabsf(value)>slot->peak)slot->peak=fabsf(value);
        }
        if(slot->fade)slot->fade--;
    }
    slot->frames+=available;if(mode)slot->replaced++;return result;
}
EXPORT unsigned LabDownstreamCount(void){return downstream_count;}
EXPORT uint64_t LabDownstreamStatistics(unsigned index,unsigned field){if(index>=(unsigned)downstream_count)return 0;Downstream* s=&downstream[index];switch(field){case 0:return(uintptr_t)s->object;case 1:return s->sampler.stem;case 2:return s->calls;case 3:return s->replaced;case 4:return s->frames;case 5:return s->position;default:return 0;}}
EXPORT double LabDownstreamEnergy(unsigned index){return index<(unsigned)downstream_count?downstream[index].energy:0;}
EXPORT void LabReleaseShared(void* control){
    // Only used for an independently factory-created reference owned by the lab.
    // Mirrors the observed MSVC shared_ptr release sequence; never deck-owned refs.
    if(!control)return;volatile LONG* strong=(volatile LONG*)((unsigned char*)control+8);
    volatile LONG* weak=(volatile LONG*)((unsigned char*)control+12);
    if(InterlockedDecrement(strong)==0){void** table=*(void***)control;((void(*)(void*))table[0])(control);if(InterlockedDecrement(weak)==0)((void(*)(void*))table[1])(control);}
}
// DLL stays loaded until the owned Traktor process exits; no unsafe manual unload.
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID reserved){return TRUE;}
