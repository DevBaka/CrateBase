#include <stddef.h>
#include <stdio.h>
#include "onnxruntime_c_api.h"
#define SHOW(name) printf("%s=%zu\n",#name,offsetof(OrtApi,name)/sizeof(void*))
int main(){
    SHOW(CreateSessionOptions);SHOW(CreateSession);SHOW(CreateSessionFromArray);SHOW(Run);
    SHOW(SetSessionExecutionMode);SHOW(DisableMemPattern);SHOW(SetSessionGraphOptimizationLevel);
    SHOW(GetErrorMessage);SHOW(ReleaseStatus);SHOW(SetIntraOpNumThreads);
    SHOW(SessionGetInputCount);SHOW(SessionGetOutputCount);SHOW(GetTensorTypeAndShape);
    SHOW(GetDimensionsCount);SHOW(GetDimensions);SHOW(GetTensorMutableData);
    SHOW(ReleaseTensorTypeAndShapeInfo);SHOW(EnableProfiling);SHOW(SessionEndProfiling);
    printf("OrtApi_slots=%zu\n",sizeof(OrtApi)/sizeof(void*));
}
