// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// precode.cpp
//

//
// Stub that runs before the actual native code
//


#include "common.h"
#include "dllimportcallback.h"

#ifdef FEATURE_PERFMAP
#include "perfmap.h"
#endif

InterleavedLoaderHeapConfig s_stubPrecodeHeapConfig;
#ifdef HAS_FIXUP_PRECODE
InterleavedLoaderHeapConfig s_fixupStubPrecodeHeapConfig;
#endif

//==========================================================================================
// class Precode
//==========================================================================================
BOOL Precode::IsValidType(PrecodeType t)
{
    LIMITED_METHOD_CONTRACT;
    SUPPORTS_DAC;

    switch (t) {
    case PRECODE_STUB:
#ifdef HAS_NDIRECT_IMPORT_PRECODE
    case PRECODE_NDIRECT_IMPORT:
#endif // HAS_NDIRECT_IMPORT_PRECODE
#ifdef HAS_FIXUP_PRECODE
    case PRECODE_FIXUP:
#endif // HAS_FIXUP_PRECODE
#ifdef HAS_THISPTR_RETBUF_PRECODE
    case PRECODE_THISPTR_RETBUF:
#endif // HAS_THISPTR_RETBUF_PRECODE
#ifdef FEATURE_INTERPRETER
    case PRECODE_INTERPRETER:
#endif // FEATURE_INTERPRETER
    case PRECODE_UMENTRY_THUNK:
        return TRUE;
    default:
        return FALSE;
    }
}

UMEntryThunk* Precode::AsUMEntryThunk()
{
    LIMITED_METHOD_CONTRACT;
    SUPPORTS_DAC;

    return dac_cast<PTR_UMEntryThunk>(this);
}

SIZE_T Precode::SizeOf(PrecodeType t)
{
    LIMITED_METHOD_CONTRACT;
    SUPPORTS_DAC;

    switch (t)
    {
    case PRECODE_STUB:
        return sizeof(StubPrecode);
#ifdef HAS_NDIRECT_IMPORT_PRECODE
    case PRECODE_NDIRECT_IMPORT:
        return sizeof(NDirectImportPrecode);
#endif // HAS_NDIRECT_IMPORT_PRECODE
#ifdef HAS_FIXUP_PRECODE
    case PRECODE_FIXUP:
        return sizeof(FixupPrecode);
#endif // HAS_FIXUP_PRECODE
#ifdef HAS_THISPTR_RETBUF_PRECODE
    case PRECODE_THISPTR_RETBUF:
        return sizeof(ThisPtrRetBufPrecode);
#endif // HAS_THISPTR_RETBUF_PRECODE
#ifdef FEATURE_INTERPRETER
    case PRECODE_INTERPRETER:
        return sizeof(InterpreterPrecode);
#endif // FEATURE_INTERPRETER

    default:
        UnexpectedPrecodeType("Precode::SizeOf", t);
        break;
    }
    return 0;
}

// Note: This is immediate target of the precode. It does not follow jump stub if there is one.
PCODE Precode::GetTarget()
{
    LIMITED_METHOD_CONTRACT;
    SUPPORTS_DAC;

    PCODE target = 0;

    PrecodeType precodeType = GetType();
    switch (precodeType)
    {
    case PRECODE_STUB:
        target = AsStubPrecode()->GetTarget();
        break;
#ifdef HAS_FIXUP_PRECODE
    case PRECODE_FIXUP:
        target = AsFixupPrecode()->GetTarget();
        break;
#endif // HAS_FIXUP_PRECODE
#ifdef HAS_THISPTR_RETBUF_PRECODE
    case PRECODE_THISPTR_RETBUF:
        target = AsThisPtrRetBufPrecode()->GetTarget();
        break;
#endif // HAS_THISPTR_RETBUF_PRECODE

    default:
        UnexpectedPrecodeType("Precode::GetTarget", precodeType);
        break;
    }
    return target;
}

MethodDesc* Precode::GetMethodDesc(BOOL fSpeculative /*= FALSE*/)
{
    CONTRACTL {
        NOTHROW;
        GC_NOTRIGGER;
        SUPPORTS_DAC;
    } CONTRACTL_END;

    TADDR pMD = (TADDR)NULL;

    PrecodeType precodeType = GetType();
    switch (precodeType)
    {
    case PRECODE_STUB:
        pMD = AsStubPrecode()->GetMethodDesc();
        break;
#ifdef HAS_NDIRECT_IMPORT_PRECODE
    case PRECODE_NDIRECT_IMPORT:
        pMD = AsNDirectImportPrecode()->GetMethodDesc();
        break;
#endif // HAS_NDIRECT_IMPORT_PRECODE
#ifdef HAS_FIXUP_PRECODE
    case PRECODE_FIXUP:
        pMD = AsFixupPrecode()->GetMethodDesc();
        break;
#endif // HAS_FIXUP_PRECODE
#ifdef HAS_THISPTR_RETBUF_PRECODE
    case PRECODE_THISPTR_RETBUF:
        pMD = AsThisPtrRetBufPrecode()->GetMethodDesc();
        break;
#endif // HAS_THISPTR_RETBUF_PRECODE
    case PRECODE_UMENTRY_THUNK:
        return NULL;
        break;
#ifdef FEATURE_INTERPRETER
    case PRECODE_INTERPRETER:
        return NULL;
        break;
#endif // FEATURE_INTERPRETER

    default:
        break;
    }

    if (pMD == (TADDR)NULL)
    {
        if (fSpeculative)
            return NULL;
        else
            UnexpectedPrecodeType("Precode::GetMethodDesc", precodeType);
    }

    // GetMethodDesc() on platform specific precode types returns TADDR. It should return
    // PTR_MethodDesc instead. It is a workaround to resolve cyclic dependency between headers.
    // Once we headers factoring of headers cleaned up, we should be able to get rid of it.
    return (PTR_MethodDesc)pMD;
}

BOOL Precode::IsPointingToPrestub(PCODE target)
{
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END;

    if (IsPointingTo(target, GetPreStubEntryPoint()))
        return TRUE;

#ifdef HAS_FIXUP_PRECODE
    if (IsPointingTo(target, ((PCODE)this + FixupPrecode::FixupCodeOffset)))
        return TRUE;
#endif

    return FALSE;
}

// If addr is patched fixup precode, returns address that it points to. Otherwise returns NULL.
PCODE Precode::TryToSkipFixupPrecode(PCODE addr)
{
    CONTRACTL {
        NOTHROW;
        GC_NOTRIGGER;
    } CONTRACTL_END;

    return 0;
}

#ifndef DACCESS_COMPILE

#ifdef FEATURE_INTERPRETER
InterpreterPrecode* Precode::AllocateInterpreterPrecode(PCODE byteCode,
                                                        LoaderAllocator *  pLoaderAllocator,
                                                        AllocMemTracker *  pamTracker)
{
    CONTRACTL
    {
        THROWS;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END;

    InterpreterPrecode* pPrecode = (InterpreterPrecode*)pamTracker->Track(pLoaderAllocator->GetNewStubPrecodeHeap()->AllocStub());
    pPrecode->Init(pPrecode, byteCode);
#ifdef FEATURE_PERFMAP
    PerfMap::LogStubs(__FUNCTION__, "UMEntryThunk", (PCODE)pPrecode, sizeof(InterpreterPrecode), PerfMapStubType::IndividualWithinBlock);
#endif
    return pPrecode;
}
#endif // FEATURE_INTERPRETER

Precode* Precode::Allocate(PrecodeType t, MethodDesc* pMD,
                           LoaderAllocator *  pLoaderAllocator,
                           AllocMemTracker *  pamTracker)
{
    CONTRACTL
    {
        THROWS;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END;

    Precode* pPrecode = NULL;

    if (t == PRECODE_FIXUP)
    {
        pPrecode = (Precode*)pamTracker->Track(pLoaderAllocator->GetFixupPrecodeHeap()->AllocStub());
        pPrecode->Init(pPrecode, t, pMD, pLoaderAllocator);
#ifdef FEATURE_PERFMAP
        PerfMap::LogStubs(__FUNCTION__, "FixupPrecode", (PCODE)pPrecode, sizeof(FixupPrecode), PerfMapStubType::IndividualWithinBlock);
#endif
    }
#ifdef HAS_THISPTR_RETBUF_PRECODE
    else if (t == PRECODE_THISPTR_RETBUF)
    {
        ThisPtrRetBufPrecode* pThisPtrRetBufPrecode = (ThisPtrRetBufPrecode*)pamTracker->Track(pLoaderAllocator->GetNewStubPrecodeHeap()->AllocStub());
        ThisPtrRetBufPrecodeData *pData = (ThisPtrRetBufPrecodeData*)pamTracker->Track(pLoaderAllocator->GetLowFrequencyHeap()->AllocMem(S_SIZE_T(sizeof(ThisPtrRetBufPrecodeData))));
        pThisPtrRetBufPrecode->Init(pData, pMD, pLoaderAllocator);
        pPrecode = (Precode*)pThisPtrRetBufPrecode;
#ifdef FEATURE_PERFMAP
        PerfMap::LogStubs(__FUNCTION__, "ThisPtrRetBuf", (PCODE)pPrecode, sizeof(ThisPtrRetBufPrecodeData), PerfMapStubType::IndividualWithinBlock);
#endif
        }
#endif // HAS_THISPTR_RETBUF_PRECODE
    else
    {
        _ASSERTE(t == PRECODE_STUB || t == PRECODE_NDIRECT_IMPORT);
        pPrecode = (Precode*)pamTracker->Track(pLoaderAllocator->GetNewStubPrecodeHeap()->AllocStub());
        pPrecode->Init(pPrecode, t, pMD, pLoaderAllocator);
#ifdef FEATURE_PERFMAP
        PerfMap::LogStubs(__FUNCTION__, t == PRECODE_STUB ? "StubPrecode" : "PInvokeImportPrecode", (PCODE)pPrecode, sizeof(StubPrecode), PerfMapStubType::IndividualWithinBlock);
#endif
    }

    return pPrecode;
}

void Precode::Init(Precode* pPrecodeRX, PrecodeType t, MethodDesc* pMD, LoaderAllocator *pLoaderAllocator)
{
    LIMITED_METHOD_CONTRACT;

    switch (t) {
    case PRECODE_STUB:
        ((StubPrecode*)this)->Init((StubPrecode*)pPrecodeRX, (TADDR)pMD, pLoaderAllocator);
        break;
#ifdef HAS_NDIRECT_IMPORT_PRECODE
    case PRECODE_NDIRECT_IMPORT:
        ((NDirectImportPrecode*)this)->Init((NDirectImportPrecode*)pPrecodeRX, pMD, pLoaderAllocator);
        break;
#endif // HAS_NDIRECT_IMPORT_PRECODE
#ifdef HAS_FIXUP_PRECODE
    case PRECODE_FIXUP:
        ((FixupPrecode*)this)->Init((FixupPrecode*)pPrecodeRX, pMD, pLoaderAllocator);
        break;
#endif // HAS_FIXUP_PRECODE
#ifdef HAS_THISPTR_RETBUF_PRECODE
    case PRECODE_THISPTR_RETBUF:
        ((ThisPtrRetBufPrecode*)this)->Init(pMD, pLoaderAllocator);
        break;
#endif // HAS_THISPTR_RETBUF_PRECODE
    default:
        UnexpectedPrecodeType("Precode::Init", t);
        break;
    }

    _ASSERTE(IsValidType(GetType()));
}

void Precode::ResetTargetInterlocked()
{
    WRAPPER_NO_CONTRACT;

    PrecodeType precodeType = GetType();
    switch (precodeType)
    {
        case PRECODE_STUB:
            AsStubPrecode()->ResetTargetInterlocked();
            break;

#ifdef HAS_FIXUP_PRECODE
        case PRECODE_FIXUP:
            AsFixupPrecode()->ResetTargetInterlocked();
            break;
#endif // HAS_FIXUP_PRECODE

        default:
            UnexpectedPrecodeType("Precode::ResetTargetInterlocked", precodeType);
            break;
    }

    // Although executable code is modified on x86/x64, a FlushInstructionCache() is not necessary on those platforms due to the
    // interlocked operation above (see ClrFlushInstructionCache())
}

BOOL Precode::SetTargetInterlocked(PCODE target, BOOL fOnlyRedirectFromPrestub)
{
    WRAPPER_NO_CONTRACT;
    _ASSERTE(!IsPointingToPrestub(target));

    PCODE expected = GetTarget();
    BOOL ret = FALSE;

    if (fOnlyRedirectFromPrestub && !IsPointingToPrestub(expected))
        return FALSE;

    PrecodeType precodeType = GetType();
    switch (precodeType)
    {
    case PRECODE_STUB:
        ret = AsStubPrecode()->SetTargetInterlocked(target, expected);
        break;

#ifdef HAS_FIXUP_PRECODE
    case PRECODE_FIXUP:
        ret = AsFixupPrecode()->SetTargetInterlocked(target, expected);
        break;
#endif // HAS_FIXUP_PRECODE

#ifdef HAS_THISPTR_RETBUF_PRECODE
    case PRECODE_THISPTR_RETBUF:
        ret = AsThisPtrRetBufPrecode()->SetTargetInterlocked(target, expected);
        break;
#endif // HAS_THISPTR_RETBUF_PRECODE

    default:
        UnexpectedPrecodeType("Precode::SetTargetInterlocked", precodeType);
        break;
    }

    // Although executable code is modified on x86/x64, a FlushInstructionCache() is not necessary on those platforms due to the
    // interlocked operation above (see ClrFlushInstructionCache())

    return ret;
}

void Precode::Reset()
{
    WRAPPER_NO_CONTRACT;

    MethodDesc* pMD = GetMethodDesc();

    PrecodeType t = GetType();
    SIZE_T size = Precode::SizeOf(t);

    switch (t)
    {
    case PRECODE_STUB:
#ifdef HAS_NDIRECT_IMPORT_PRECODE
    case PRECODE_NDIRECT_IMPORT:
#endif // HAS_NDIRECT_IMPORT_PRECODE
#ifdef HAS_FIXUP_PRECODE
    case PRECODE_FIXUP:
#endif // HAS_FIXUP_PRECODE
#ifdef HAS_THISPTR_RETBUF_PRECODE
    case PRECODE_THISPTR_RETBUF:
#endif // HAS_THISPTR_RETBUF_PRECODE
        Init(this, t, pMD, pMD->GetLoaderAllocator());
        break;

    default:
        _ASSERTE(!"Unexpected precode type");
        JIT_FailFast();
        break;
    }
}

#endif // !DACCESS_COMPILE

#ifdef DACCESS_COMPILE
void Precode::EnumMemoryRegions(CLRDataEnumMemoryFlags flags)
{
    SUPPORTS_DAC;
    PrecodeType t = GetType();

    DacEnumMemoryRegion(GetStart(), SizeOf(t));
}
#endif

#ifdef HAS_FIXUP_PRECODE

#ifdef DACCESS_COMPILE
void FixupPrecode::EnumMemoryRegions(CLRDataEnumMemoryFlags flags)
{
    SUPPORTS_DAC;
    DacEnumMemoryRegion(dac_cast<TADDR>(this), sizeof(FixupPrecode));
    DacEnumMemoryRegion(dac_cast<TADDR>(GetData()), sizeof(FixupPrecodeData));
}
#endif // DACCESS_COMPILE

#endif // HAS_FIXUP_PRECODE

#ifndef DACCESS_COMPILE

#ifdef HAS_THISPTR_RETBUF_PRECODE
extern "C" void ThisPtrRetBufPrecodeWorker();
void ThisPtrRetBufPrecode::Init(ThisPtrRetBufPrecodeData* pPrecodeData, MethodDesc* pMD, LoaderAllocator *pLoaderAllocator)
{
    StubPrecode::Init(this, dac_cast<TADDR>(pPrecodeData), pLoaderAllocator, ThisPtrRetBufPrecode::Type, (TADDR)ThisPtrRetBufPrecodeWorker);
    pPrecodeData->MethodDesc = pMD;
    pPrecodeData->Target = GetPreStubEntryPoint();
}

void ThisPtrRetBufPrecode::Init(MethodDesc* pMD, LoaderAllocator *pLoaderAllocator)
{
    ThisPtrRetBufPrecodeData* pPrecodeData = GetData();
    pPrecodeData->MethodDesc = pMD;
    pPrecodeData->Target = GetPreStubEntryPoint();
}
#endif // HAS_THISPTR_RETBUF_PRECODE

void StubPrecode::Init(StubPrecode* pPrecodeRX, TADDR secretParam, LoaderAllocator *pLoaderAllocator /* = NULL */,
    TADDR type /* = StubPrecode::Type */, TADDR target /* = NULL */)
{
    WRAPPER_NO_CONTRACT;

    StubPrecodeData *pStubData = GetData();

    if (pLoaderAllocator != NULL)
    {
        // Use pMD == NULL in all precode initialization methods to allocate the initial jump stub in non-dynamic heap
        // that has the same lifetime like as the precode itself
        if (target == (TADDR)NULL)
            target = GetPreStubEntryPoint();
        pStubData->Target = target;
    }

    pStubData->SecretParam = secretParam;
    pStubData->Type = type;
}

#if defined(TARGET_ARM64) && defined(TARGET_UNIX)
    #define ENUM_PAGE_SIZE(size) \
        extern "C" void StubPrecodeCode##size(); \
        extern "C" void StubPrecodeCode##size##_End();
    ENUM_PAGE_SIZES
    #undef ENUM_PAGE_SIZE
#else
extern "C" void StubPrecodeCode();
extern "C" void StubPrecodeCode_End();
#endif

#ifdef TARGET_X86
extern "C" size_t StubPrecodeCode_MethodDesc_Offset;
extern "C" size_t StubPrecodeCode_Target_Offset;

#define SYMBOL_VALUE(name) ((size_t)&name)

#endif

#if defined(TARGET_ARM64) && defined(TARGET_UNIX)
void (*StubPrecode::StubPrecodeCode)();
void (*StubPrecode::StubPrecodeCode_End)();
#endif

#ifdef FEATURE_MAP_THUNKS_FROM_IMAGE
extern "C" void StubPrecodeCodeTemplate();
#else
#define StubPrecodeCodeTemplate NULL
#endif

void StubPrecode::StaticInitialize()
{
#if defined(TARGET_ARM64) && defined(TARGET_UNIX)
    #define ENUM_PAGE_SIZE(size) \
        case size: \
            StubPrecodeCode = StubPrecodeCode##size; \
            StubPrecodeCode_End = StubPrecodeCode##size##_End; \
            _ASSERTE((SIZE_T)((BYTE*)StubPrecodeCode##size##_End - (BYTE*)StubPrecodeCode##size) <= StubPrecode::CodeSize); \
            break;

    int pageSize = GetStubCodePageSize();
    switch (pageSize)
    {
        ENUM_PAGE_SIZES
        default:
            EEPOLICY_HANDLE_FATAL_ERROR_WITH_MESSAGE(COR_E_EXECUTIONENGINE, W("Unsupported OS page size"));
    }

    if (StubPrecodeCodeTemplate != NULL && pageSize != 0x4000)
    {
        // This should fail if the template is used on a platform which doesn't support the supported page size for templates
        ThrowHR(COR_E_EXECUTIONENGINE);
    }

    #undef ENUM_PAGE_SIZE
#else
    _ASSERTE((SIZE_T)((BYTE*)StubPrecodeCode_End - (BYTE*)StubPrecodeCode) <= StubPrecode::CodeSize);
#endif
#ifdef TARGET_LOONGARCH64
    _ASSERTE(((*((short*)PCODEToPINSTR((PCODE)StubPrecodeCode) + OFFSETOF_PRECODE_TYPE)) >> 5) == StubPrecode::Type);
#elif TARGET_RISCV64
    _ASSERTE((*((BYTE*)PCODEToPINSTR((PCODE)StubPrecodeCode) + OFFSETOF_PRECODE_TYPE)) == StubPrecode::Type);
#else
    _ASSERTE((*((BYTE*)PCODEToPINSTR((PCODE)StubPrecodeCode) + OFFSETOF_PRECODE_TYPE)) == StubPrecode::Type);
#endif

    InitializeLoaderHeapConfig(&s_stubPrecodeHeapConfig, StubPrecode::CodeSize, (void*)StubPrecodeCodeTemplate, StubPrecode::GenerateCodePage);
}

void StubPrecode::GenerateCodePage(uint8_t* pageBase, uint8_t* pageBaseRX, size_t pageSize)
{
#ifdef TARGET_X86
    int totalCodeSize = (pageSize / StubPrecode::CodeSize) * StubPrecode::CodeSize;
    for (int i = 0; i < totalCodeSize; i += StubPrecode::CodeSize)
    {
        memcpy(pageBase + i, (const void*)StubPrecodeCode, (uint8_t*)StubPrecodeCode_End - (uint8_t*)StubPrecodeCode);

        uint8_t* pTargetSlot = pageBaseRX + i + pageSize + offsetof(StubPrecodeData, Target);
        *(uint8_t**)(pageBase + i + SYMBOL_VALUE(StubPrecodeCode_Target_Offset)) = pTargetSlot;

        BYTE* pMethodDescSlot = pageBaseRX + i + pageSize + offsetof(StubPrecodeData, SecretParam);
        *(uint8_t**)(pageBase + i + SYMBOL_VALUE(StubPrecodeCode_MethodDesc_Offset)) = pMethodDescSlot;
    }
#else // TARGET_X86
    FillStubCodePage(pageBase, (const void*)PCODEToPINSTR((PCODE)StubPrecodeCode), StubPrecode::CodeSize, pageSize);
#endif // TARGET_X86
}

BOOL StubPrecode::IsStubPrecodeByASM(PCODE addr)
{
    BYTE *pInstr = (BYTE*)PCODEToPINSTR(addr);
#ifdef TARGET_X86
    return *pInstr == *(BYTE*)(StubPrecodeCode) &&
            *(DWORD*)(pInstr + SYMBOL_VALUE(StubPrecodeCode_MethodDesc_Offset)) == (DWORD)(pInstr + GetStubCodePageSize() + offsetof(StubPrecodeData, SecretParam)) &&
            *(WORD*)(pInstr + 5) == *(WORD*)((BYTE*)StubPrecodeCode + 5) &&
            *(DWORD*)(pInstr + SYMBOL_VALUE(StubPrecodeCode_Target_Offset)) == (DWORD)(pInstr + GetStubCodePageSize() + offsetof(StubPrecodeData, Target));
#else // TARGET_X86
    BYTE *pTemplateInstr = (BYTE*)PCODEToPINSTR((PCODE)StubPrecodeCode);
    BYTE *pTemplateInstrEnd = (BYTE*)PCODEToPINSTR((PCODE)StubPrecodeCode_End);
    while ((pTemplateInstr < pTemplateInstrEnd) && (*pInstr == *pTemplateInstr))
    {
        pInstr++;
        pTemplateInstr++;
    }

    return pTemplateInstr == pTemplateInstrEnd;
#endif // TARGET_X86
}

#ifdef FEATURE_INTERPRETER
void InterpreterPrecode::Init(InterpreterPrecode* pPrecodeRX, TADDR byteCodeAddr)
{
    WRAPPER_NO_CONTRACT;
    InterpreterPrecodeData *pStubData = GetData();

    pStubData->Target = (PCODE)InterpreterStub;
    pStubData->ByteCodeAddr = byteCodeAddr;
    pStubData->Type = InterpreterPrecode::Type;
}
#endif // FEATURE_INTERPRETER

#ifdef HAS_NDIRECT_IMPORT_PRECODE

void NDirectImportPrecode::Init(NDirectImportPrecode* pPrecodeRX, MethodDesc* pMD, LoaderAllocator *pLoaderAllocator)
{
    WRAPPER_NO_CONTRACT;
    StubPrecode::Init(pPrecodeRX, (TADDR)pMD, pLoaderAllocator, NDirectImportPrecode::Type, GetEEFuncEntryPoint(NDirectImportThunk));
}

#endif // HAS_NDIRECT_IMPORT_PRECODE

#ifdef HAS_FIXUP_PRECODE
void FixupPrecode::Init(FixupPrecode* pPrecodeRX, MethodDesc* pMD, LoaderAllocator *pLoaderAllocator)
{
    WRAPPER_NO_CONTRACT;

    _ASSERTE(pPrecodeRX == this);

    FixupPrecodeData *pData = GetData();
    pData->MethodDesc = pMD;

    _ASSERTE(GetMethodDesc() == (TADDR)pMD);

    pData->Target = (PCODE)pPrecodeRX + FixupPrecode::FixupCodeOffset;
    pData->PrecodeFixupThunk = GetPreStubEntryPoint();
}

#if defined(TARGET_ARM64) && defined(TARGET_UNIX)
    #define ENUM_PAGE_SIZE(size) \
        extern "C" void FixupPrecodeCode##size(); \
        extern "C" void FixupPrecodeCode##size##_End();
    ENUM_PAGE_SIZES
    #undef ENUM_PAGE_SIZE
#else
extern "C" void FixupPrecodeCode();
extern "C" void FixupPrecodeCode_End();
#endif

#ifdef TARGET_X86
extern "C" size_t FixupPrecodeCode_MethodDesc_Offset;
extern "C" size_t FixupPrecodeCode_Target_Offset;
extern "C" size_t FixupPrecodeCode_PrecodeFixupThunk_Offset;
#endif

#if defined(TARGET_ARM64) && defined(TARGET_UNIX)
void (*FixupPrecode::FixupPrecodeCode)();
void (*FixupPrecode::FixupPrecodeCode_End)();
#endif

#ifdef FEATURE_MAP_THUNKS_FROM_IMAGE
extern "C" void FixupPrecodeCodeTemplate();
#else
#define FixupPrecodeCodeTemplate NULL
#endif

void FixupPrecode::StaticInitialize()
{
#if defined(TARGET_ARM64) && defined(TARGET_UNIX)
    #define ENUM_PAGE_SIZE(size) \
        case size: \
            FixupPrecodeCode = FixupPrecodeCode##size; \
            FixupPrecodeCode_End = FixupPrecodeCode##size##_End; \
            _ASSERTE((SIZE_T)((BYTE*)FixupPrecodeCode##size##_End - (BYTE*)FixupPrecodeCode##size) <= FixupPrecode::CodeSize); \
            break;

    int pageSize = GetStubCodePageSize();

    switch (pageSize)
    {
        ENUM_PAGE_SIZES
        default:
            EEPOLICY_HANDLE_FATAL_ERROR_WITH_MESSAGE(COR_E_EXECUTIONENGINE, W("Unsupported OS page size"));
    }
    #undef ENUM_PAGE_SIZE

    if (FixupPrecodeCodeTemplate != NULL && pageSize != 0x4000)
    {
        // This should fail if the template is used on a platform which doesn't support the supported page size for templates
        ThrowHR(COR_E_EXECUTIONENGINE);
    }
#else
    _ASSERTE((SIZE_T)((BYTE*)FixupPrecodeCode_End - (BYTE*)FixupPrecodeCode) <= FixupPrecode::CodeSize);
#endif
#ifdef TARGET_LOONGARCH64
    _ASSERTE(((*((short*)PCODEToPINSTR((PCODE)StubPrecodeCode) + OFFSETOF_PRECODE_TYPE)) >> 5) == StubPrecode::Type);
#elif TARGET_RISCV64
    _ASSERTE((*((BYTE*)PCODEToPINSTR((PCODE)FixupPrecodeCode) + OFFSETOF_PRECODE_TYPE)) == FixupPrecode::Type);
#else
    _ASSERTE(*((BYTE*)PCODEToPINSTR((PCODE)FixupPrecodeCode) + OFFSETOF_PRECODE_TYPE) == FixupPrecode::Type);
#endif

    InitializeLoaderHeapConfig(&s_fixupStubPrecodeHeapConfig, FixupPrecode::CodeSize, (void*)FixupPrecodeCodeTemplate, FixupPrecode::GenerateCodePage);
}

void FixupPrecode::GenerateCodePage(uint8_t* pageBase, uint8_t* pageBaseRX, size_t pageSize)
{
#ifdef TARGET_X86
    int totalCodeSize = (pageSize / FixupPrecode::CodeSize) * FixupPrecode::CodeSize;

    for (int i = 0; i < totalCodeSize; i += FixupPrecode::CodeSize)
    {
        memcpy(pageBase + i, (const void*)FixupPrecodeCode, FixupPrecode::CodeSize);
        uint8_t* pTargetSlot = pageBaseRX + i + pageSize + offsetof(FixupPrecodeData, Target);
        *(uint8_t**)(pageBase + i + SYMBOL_VALUE(FixupPrecodeCode_Target_Offset)) = pTargetSlot;

        BYTE* pMethodDescSlot = pageBaseRX + i + pageSize + offsetof(FixupPrecodeData, MethodDesc);
        *(uint8_t**)(pageBase + i + SYMBOL_VALUE(FixupPrecodeCode_MethodDesc_Offset)) = pMethodDescSlot;

        BYTE* pPrecodeFixupThunkSlot = pageBaseRX + i + pageSize + offsetof(FixupPrecodeData, PrecodeFixupThunk);
        *(uint8_t**)(pageBase + i + SYMBOL_VALUE(FixupPrecodeCode_PrecodeFixupThunk_Offset)) = pPrecodeFixupThunkSlot;
    }
#else // TARGET_X86
    FillStubCodePage(pageBase, (const void*)PCODEToPINSTR((PCODE)FixupPrecodeCode), FixupPrecode::CodeSize, pageSize);
#endif // TARGET_X86
}

BOOL FixupPrecode::IsFixupPrecodeByASM(PCODE addr)
{
    BYTE *pInstr = (BYTE*)PCODEToPINSTR(addr);
#ifdef TARGET_X86
    return
        *(WORD*)(pInstr) == *(WORD*)(FixupPrecodeCode) &&
        *(DWORD*)(pInstr + SYMBOL_VALUE(FixupPrecodeCode_Target_Offset)) == (DWORD)(pInstr + GetStubCodePageSize() + offsetof(FixupPrecodeData, Target)) &&
        *(pInstr + 6) == *((BYTE*)FixupPrecodeCode + 6) &&
        *(DWORD*)(pInstr + SYMBOL_VALUE(FixupPrecodeCode_MethodDesc_Offset)) == (DWORD)(pInstr + GetStubCodePageSize() + offsetof(FixupPrecodeData, MethodDesc)) &&
        *(WORD*)(pInstr + 11) == *(WORD*)((BYTE*)FixupPrecodeCode + 11) &&
        *(DWORD*)(pInstr + SYMBOL_VALUE(FixupPrecodeCode_PrecodeFixupThunk_Offset)) == (DWORD)(pInstr + GetStubCodePageSize() + offsetof(FixupPrecodeData, PrecodeFixupThunk));
#else // TARGET_X86
    BYTE *pTemplateInstr = (BYTE*)PCODEToPINSTR((PCODE)FixupPrecodeCode);
    BYTE *pTemplateInstrEnd = (BYTE*)PCODEToPINSTR((PCODE)FixupPrecodeCode_End);
    while ((pTemplateInstr < pTemplateInstrEnd) && (*pInstr == *pTemplateInstr))
    {
        pInstr++;
        pTemplateInstr++;
    }

    return pTemplateInstr == pTemplateInstrEnd;
#endif // TARGET_X86
}

#endif // HAS_FIXUP_PRECODE

BOOL DoesSlotCallPrestub(PCODE pCode)
{
    CONTRACTL {
        NOTHROW;
        GC_NOTRIGGER;
        PRECONDITION(pCode != GetPreStubEntryPoint());
    } CONTRACTL_END;

    TADDR pInstr = dac_cast<TADDR>(PCODEToPINSTR(pCode));

    if (!IS_ALIGNED(pInstr, PRECODE_ALIGNMENT))
    {
        return FALSE;
    }

    //FixupPrecode
#if defined(HAS_FIXUP_PRECODE)
    if (FixupPrecode::IsFixupPrecodeByASM(pCode))
    {
        PCODE pTarget = dac_cast<PTR_FixupPrecode>(pInstr)->GetTarget();

        return pTarget == PCODEToPINSTR(pCode) + FixupPrecode::FixupCodeOffset;
    }
#endif

    // StubPrecode
    if (StubPrecode::IsStubPrecodeByASM(pCode))
    {
        pCode = dac_cast<PTR_StubPrecode>(pInstr)->GetTarget();
        return pCode == GetPreStubEntryPoint();
    }

    return FALSE;
}

void PrecodeMachineDescriptor::Init(PrecodeMachineDescriptor *dest)
{
    dest->OffsetOfPrecodeType = OFFSETOF_PRECODE_TYPE;
    // cDAC will do (where N = 8*ReadWidthOfPrecodeType):
    //   uintN_t PrecodeType = *(uintN_t*)(pPrecode + OffsetOfPrecodeType);
    //   PrecodeType >>= ShiftOfPrecodeType;
    //   return (byte)PrecodeType;
#ifdef TARGET_LOONGARCH64
    dest->ReadWidthOfPrecodeType = 2;
#else
    dest->ReadWidthOfPrecodeType = 1;
#endif
#if defined(SHIFTOF_PRECODE_TYPE)
    dest->ShiftOfPrecodeType = SHIFTOF_PRECODE_TYPE;
#else
    dest->ShiftOfPrecodeType = 0;
#endif

    dest->InvalidPrecodeType = InvalidPrecode::Type;
    dest->StubPrecodeType = StubPrecode::Type;
#ifdef HAS_NDIRECT_IMPORT_PRECODE
    dest->PInvokeImportPrecodeType = NDirectImportPrecode::Type;
#endif // HAS_NDIRECT_IMPORT_PRECODE
#ifdef HAS_FIXUP_PRECODE
    dest->FixupPrecodeType = FixupPrecode::Type;
#endif
#ifdef HAS_THISPTR_RETBUF_PRECODE
    dest->ThisPointerRetBufPrecodeType = ThisPtrRetBufPrecode::Type;
#endif
    dest->StubCodePageSize = GetStubCodePageSize();
}

#endif // !DACCESS_COMPILE

