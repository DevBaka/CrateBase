// Private, version-pinned lab experiment. No installation patches or DSP/model work here.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <string.h>

#define EXPORT __declspec(dllexport)
#define MAX_READERS 256
#define BLOCK 1024
typedef int64_t (*ReadFn)(void*, float**);
typedef struct {
    char magic[8]; uint32_t version, sample_rate, block_frames, channels;
    uint64_t total_frames, block_count, data_offset;
    volatile LONG ready_blocks, requested_block, error_code, generation;
    uint64_t original_offset; volatile LONG original_ready;
} CacheHeader;
typedef struct {
    void* object; ReadFn original; void** original_vtable;
    void* table[19]; unsigned stem; uint64_t calls, replaced, fallback;
} Reader;
static Reader readers[MAX_READERS];
static volatile LONG reader_count;
static HANDLE file_handle, mapping_handle;
static unsigned char* mapping;
static CacheHeader* header;
static volatile LONG enabled=1;

static Reader* find_reader(void* self) {
    LONG count=reader_count;
    for(LONG i=0;i<count;i++)if(readers[i].object==self)return &readers[i];
    return 0;
}
static int64_t read_cached(void* self,float** outputs) {
    Reader* reader=find_reader(self);
    if(!reader)return 0;
    // Verified 4.5.1 MediaAudioStream position before read, in source sample frames.
    int64_t position=*(int64_t*)((unsigned char*)self+0xe0);
    reader->calls++;
    if(!header||position<0||!outputs||!outputs[0]||!outputs[1])return reader->original(self,outputs);
    int64_t result=BLOCK;
    uint64_t block=(uint64_t)position/BLOCK, within=(uint64_t)position%BLOCK;
    if(block>=header->block_count||within+(uint64_t)result>BLOCK)return reader->original(self,outputs);
    volatile LONG* ready=(volatile LONG*)(mapping+4096);
    // Acquire barrier pairs with the worker's data-before-ready publication.
    LONG available=InterlockedCompareExchange((volatile LONG*)&ready[block],0,0);
    if(available&&InterlockedCompareExchange(&enabled,0,0)) {
        const float* source=(const float*)(mapping+header->data_offset)+block*8*BLOCK+(reader->stem-1)*2*BLOCK;
        memcpy(outputs[0],source+within,result*sizeof(float));
        memcpy(outputs[1],source+BLOCK+within,result*sizeof(float));
        reader->replaced++;
    } else {
        reader->fallback++;
        InterlockedExchange(&header->requested_block,(LONG)block);
        if(header->original_offset&&InterlockedCompareExchange(&header->original_ready,0,0)) {
            const float* original=(const float*)(mapping+header->original_offset);
            uint64_t remaining=header->total_frames>(uint64_t)position?header->total_frames-position:0;
            for(int ch=0;ch<2;ch++)for(int64_t i=0;i<result;i++)
                outputs[ch][i]=(uint64_t)i<remaining?original[ch*header->total_frames+position+i]*.25f:0.f;
        } else return reader->original(self,outputs);
    }
    // Keep the verified native source position/seek contract while avoiding a
    // second decoder pass. If we ever delegate again, force its decoder to seek.
    *(int64_t*)((unsigned char*)self+0xe0)=position+BLOCK;
    *(unsigned char*)((unsigned char*)self+0xd0)=1;
    *(uint32_t*)((unsigned char*)self+0xd4)=0;
    return result;
}
EXPORT int LabConfigure(const wchar_t* path) {
    if(mapping)return -1; // One immutable mapping per experiment/process.
    file_handle=CreateFileW(path,GENERIC_READ|GENERIC_WRITE,FILE_SHARE_READ|FILE_SHARE_WRITE,0,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,0);
    if(file_handle==INVALID_HANDLE_VALUE)return (int)GetLastError();
    mapping_handle=CreateFileMappingW(file_handle,0,PAGE_READWRITE,0,0,0);
    if(!mapping_handle)return (int)GetLastError();
    mapping=(unsigned char*)MapViewOfFile(mapping_handle,FILE_MAP_READ|FILE_MAP_WRITE,0,0,0);
    if(!mapping)return (int)GetLastError();
    header=(CacheHeader*)mapping;
    if(memcmp(header->magic,"CBSTEM01",8)||header->version!=1||header->block_frames!=BLOCK||header->channels!=8)return -2;
    return 0;
}
EXPORT int LabRegister(void* object,unsigned stem) {
    if(!header||!object||stem<1||stem>4)return -1;
    Reader* reader=find_reader(object);
    if(reader&&*(void***)object==reader->table+1)return 0;
    if(!reader) {
        LONG index=reader_count;if(index>=MAX_READERS)return -2;
        reader=&readers[index];memset(reader,0,sizeof(*reader));
        reader->object=object;InterlockedExchange(&reader_count,index+1);
    }
    reader->stem=stem;reader->original_vtable=*(void***)object;
    reader->original=(ReadFn)reader->original_vtable[17];
    memcpy(reader->table,reader->original_vtable-1,19*sizeof(void*));
    reader->table[18]=(void*)read_cached;
    InterlockedExchangePointer((void* volatile*)object,reader->table+1);
    return 0;
}
EXPORT uint64_t LabStatistics(unsigned index,unsigned field) {
    if(index>=(unsigned)reader_count)return 0;
    Reader* reader=&readers[index];
    switch(field){case 0:return (uintptr_t)reader->object;case 1:return reader->stem;case 2:return reader->calls;case 3:return reader->replaced;case 4:return reader->fallback;default:return 0;}
}
EXPORT unsigned LabReaderCount(void){return (unsigned)reader_count;}
EXPORT void LabSetEnabled(int value){InterlockedExchange(&enabled,value?1:0);}
// Deliberately no unload API: vtables remain valid until the owned lab process exits.
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID reserved){return TRUE;}
