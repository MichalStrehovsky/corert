// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#include "common.h"

#include "sal.h"
#include "gcenv.structs.h"
#include "gcenv.base.h"

#include <stdlib.h> 
#include "vector.h"

#ifndef CPPCODEGEN
NoStl::vector<void*> __myVector;
//
// This is the mechanism whereby multiple linked modules contribute their global data for initialization at
// startup of the application.
//
// Sections are created in the output obj file to mark the beginning and end of merged global data. They 
// are named .modules$A and .modules$Z. Each section defines a sentinel symbol that is used to
// get the addresses of the start and end of global data at runtime.
//
// Each obj file compiled from managed code has a .modules$I section containing a pointer to its ReadyToRun
// data (which points at eager class constructors, frozen strings, etc).
//
// On Windows, the #pragma ... /merge directive folds the book-end sections and all .modules$I sections from all input
// obj files into .rdata in alphabetical order.
//
// On Linux, a linker script is used (see src/scripts/linkerscript) to do the equivalent since there is no similar
// #pragma directive.
//
// On OSX, neither of these options are available but we can use a feature of MachO sections that invokes
// each pointer in the section as a function to achieve the same effect with the helper RegisterReadyToRunModule
//
#if defined(_MSC_VER)

#pragma section(".modules$A", read)
#pragma section(".modules$Z", read)
extern "C" __declspec(allocate(".modules$A")) void * __modules_a[];
extern "C" __declspec(allocate(".modules$Z")) void * __modules_z[];

__declspec(allocate(".modules$A")) void * __modules_a[] = { nullptr };
__declspec(allocate(".modules$Z")) void * __modules_z[] = { nullptr };
#pragma comment(linker, "/merge:.modules=.rdata")

// Sentinels for managed code section are not implemented here because of the C++ compiler 
// wraps them with a jump stub in debug builds. They are emitted in ilc instead.
extern "C" void __managedcode_a();
extern "C" void __managedcode_z();

#else // _MSC_VER

#if defined(__APPLE__)

//
// On platforms with MachO binaries (OSX), we cannot use the above mechanism to register
// global data for each module. Instead, we use a special section type whose contents
// are a list of function pointers to call.  We create a jump stub that passes the global
// data pointer into RegisterReadyToRunModule where we save it in a list for the startup
// code to consume.
//
NoStl::vector<void*> __registeredModules;

extern "C" void RegisterReadyToRunModule(void * pModule)
{
    __registeredModules.push_back(pModule);
}

#else // __APPLE__

extern "C" __attribute((section (".modules$A"))) void * __modules_a[];
extern "C" __attribute((section (".modules$Z"))) void * __modules_z[];
__attribute((section (".modules$A"))) void * __modules_a[] = { nullptr };
__attribute((section (".modules$Z"))) void * __modules_z[] = { nullptr };

#endif // __APPLE__

#endif // _MSC_VER

#endif // CPPCODEGEN

#pragma warning(disable:4297)

extern "C" Object * RhNewObject(MethodTable * pMT);
extern "C" Object * RhNewArray(MethodTable * pMT, int32_t elements);
extern "C" void * RhTypeCast_IsInstanceOf(void * pObject, MethodTable * pMT);
extern "C" void * RhTypeCast_CheckCast(void * pObject, MethodTable * pMT);
extern "C" void RhpStelemRef(void * pArray, int index, void * pObj);
extern "C" void * RhpLdelemaRef(void * pArray, int index, MethodTable * pMT);
extern "C" __NORETURN void RhpThrowEx(void * pEx);

#ifdef CPPCODEGEN

extern "C" Object * __allocate_object(MethodTable * pMT)
{
    return RhNewObject(pMT);
}

extern "C" Object * __allocate_array(size_t elements, MethodTable * pMT)
{
    return RhNewArray(pMT, (int32_t)elements); // TODO: type mismatch
}

extern "C" Object * __castclass(void * obj, MethodTable * pTargetMT)
{
    return (Object *)RhTypeCast_CheckCast(obj, pTargetMT);
}

extern "C" Object * __isinst(void * obj, MethodTable * pTargetMT)
{
    return (Object *)RhTypeCast_IsInstanceOf(obj, pTargetMT);
}

extern "C" void __stelem_ref(void * pArray, unsigned idx, void * obj)
{
    RhpStelemRef(pArray, idx, obj);
}

extern "C" void* __ldelema_ref(void * pArray, unsigned idx, MethodTable * type)
{
    return RhpLdelemaRef(pArray, idx, type);
}

extern "C" void __throw_exception(void * pEx)
{
    RhpThrowEx(pEx);
}

void __range_check_fail()
{
    throw "ThrowRangeOverflowException";
}
#endif // CPPCODEGEN


extern "C" void RhpReversePInvoke2(ReversePInvokeFrame* pRevFrame);
extern "C" void RhpReversePInvokeReturn(ReversePInvokeFrame* pRevFrame);
extern "C" int32_t RhpEnableConservativeStackReporting();

extern "C" bool RhpRegisterCoffModule(void * pModule, 
    void * pvStartRange, uint32_t cbRange,
    void ** pClasslibFunctions, uint32_t nClasslibFunctions);

#define DLL_PROCESS_ATTACH      1
extern "C" BOOL WINAPI RtuDllMain(HANDLE hPalInstance, DWORD dwReason, void* pvReserved);

REDHAWK_PALIMPORT bool REDHAWK_PALAPI PalInit();

int __initialize_runtime()
{
    if (!PalInit())
        return -1;

    if (!RtuDllMain(NULL, DLL_PROCESS_ATTACH, NULL))
        return -1;

    if (!RhpEnableConservativeStackReporting())
        return -1;

    return 0;
}

void __shutdown_runtime()
{
}

void __reverse_pinvoke(ReversePInvokeFrame* pRevFrame)
{
    RhpReversePInvoke2(pRevFrame);
}

void __reverse_pinvoke_return(ReversePInvokeFrame* pRevFrame)
{
    RhpReversePInvokeReturn(pRevFrame);
}

namespace System_Private_CoreLib { namespace System { 

    class Object {
    public:
        MethodTable * get_EEType() { return *(MethodTable **)this; }
    };

    class Array : public Object {
    public:
        int32_t GetArrayLength() {
            return *(int32_t *)((void **)this + 1);
        }
        void * GetArrayData() {
            return (void **)this + 2;
        }
    };

    class String : public Object { public:
        static MethodTable * __getMethodTable();
    };

    class String__Array : public Object { public:
        static MethodTable * __getMethodTable();
    };

    class EETypePtr { public:
        intptr_t m_value;
    };

}; };

using namespace System_Private_CoreLib;

void PrintStringObject(System::String *pStringToPrint)
{
    // Get the number of characters in managed string (stored as UTF16)
    int32_t length = *((int32_t*)((char*)(pStringToPrint)+sizeof(intptr_t)));

    // Get the pointer to the start of the character array
    uint16_t *pString = (uint16_t*)((char*)(pStringToPrint)+sizeof(intptr_t) + sizeof(int32_t));
    
    // Loop to display the string
    int32_t index = 0;
    while (index < length)
    {
        putwchar(*pString);
        pString++;
        index++;
    }   
}
extern "C" void __not_yet_implemented(System::String * pMethodName, System::String * pMessage)
{
    printf("ILCompiler failed generating code for this method; execution cannot continue.\n");
    printf("This is likely because of a feature that is not yet implemented in the compiler.\n");
    printf("Method: ");
    PrintStringObject(pMethodName);
    printf("\n\n");
    printf("Reason: ");
    PrintStringObject(pMessage);
    printf("\n");

    exit(-1);
}

extern "C" void __fail_fast()
{
    // TODO: FailFast
    throw "__fail_fast";
}

#ifdef CPPCODEGEN
Object * __load_string_literal(const char * string)
{
    // TODO: Cache/intern string literals
    // TODO: Unicode string literals

    size_t len = strlen(string);

    Object * pString = RhNewArray(System::String::__getMethodTable(), (int32_t)len);

    uint16_t * p = (uint16_t *)((char*)pString + sizeof(intptr_t) + sizeof(int32_t));
    for (size_t i = 0; i < len; i++)
        p[i] = string[i];
    return pString;
}
#endif // CPPCODEGEN

#ifdef CPPCODEGEN
extern "C" void * g_pSystemArrayEETypeTemporaryWorkaround = 0;
#else
extern "C" void * __EEType_System_Private_CoreLib_System_Array;
extern "C" void * g_pSystemArrayEETypeTemporaryWorkaround = &__EEType_System_Private_CoreLib_System_Array;
#endif

#if defined(CPPCODEGEN)
extern "C" void RhpThrowEx(void * pEx)
{
    throw "RhpThrowEx";
}
extern "C" void RhpThrowHwEx()
{
    throw "RhpThrowHwEx";
}
extern "C" void RhpCallCatchFunclet()
{
    throw "RhpCallCatchFunclet";
}
extern "C" void RhpCallFilterFunclet()
{
    throw "RhpCallFilterFunclet";
}
extern "C" void RhpCallFinallyFunclet()
{
    throw "RhpCallFinallyFunclet";
}
#endif // CPPCODEGEN

extern "C" void RhGetCurrentThreadStackTrace()
{
    throw "RhGetCurrentThreadStackTrace";
}
extern "C" void RhpUniversalTransition()
{
    throw "RhpUniversalTransition";
}
extern "C" void RhpEtwExceptionThrown()
{
    throw "RhpEtwExceptionThrown";
}

#ifndef CPPCODEGEN

extern "C" void InitializeModules(void ** modules, int count);

#ifdef _WIN32
extern "C" void* WINAPI GetModuleHandleW(const wchar_t *);
#endif

extern "C" void GetRuntimeException();
extern "C" void FailFast();
extern "C" void AppendExceptionStackFrame();

typedef void(*pfn)();

static const pfn c_classlibFunctions[] = {
    &GetRuntimeException,
    &FailFast,
    nullptr, // &UnhandledExceptionHandler,
    nullptr, // &AppendExceptionStackFrame,
};

#if defined(_WIN32)
extern "C" int __managed__Main(int argc, wchar_t* argv[]);
int wmain(int argc, wchar_t* argv[])
#else
extern "C" int __managed__Main(int argc, char* argv[]);
int main(int argc, char* argv[])
#endif
{
    if (__initialize_runtime() != 0) return -1;

#if defined(_WIN32) && !defined(CPPCODEGEN)
    if (!RhpRegisterCoffModule(GetModuleHandleW(NULL),
        &__managedcode_a, (uint32_t)((char *)&__managedcode_z - (char*)&__managedcode_a),
        (void **)&c_classlibFunctions, _countof(c_classlibFunctions)))
    {
        return -1;
    }
#endif

#if defined (__APPLE__)
    InitializeModules(&__registeredModules[0], __registeredModules.size());
#else
    InitializeModules(__modules_a, (int)((__modules_z - __modules_a))); 
#endif

    int retval;
    try
    {
        retval = __managed__Main(argc, argv);
    }
    catch (const char* &e)
    {
        printf("Call to an unimplemented runtime method; execution cannot continue.\n");
        printf("Method: %s\n", e);
        retval = -1;
    }

    __shutdown_runtime();
    return retval;
}

#endif // !CPPCODEGEN
