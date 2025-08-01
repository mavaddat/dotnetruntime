// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// File: ARRAY.CPP
//

//
// File which contains a bunch of array related things.
//

#include "common.h"

#include "clsload.hpp"
#include "method.hpp"
#include "class.h"
#include "object.h"
#include "field.h"
#include "util.hpp"
#include "excep.h"
#include "siginfo.hpp"
#include "threads.h"
#include "stublink.h"
#include "stubcache.h"
#include "dllimport.h"
#include "gcdesc.h"
#include "jitinterface.h"
#include "eeconfig.h"
#include "log.h"
#include "cgensys.h"
#include "array.h"
#include "typestring.h"
#include "sigbuilder.h"

#define MAX_SIZE_FOR_VALUECLASS_IN_ARRAY 0xffff
#define MAX_PTRS_FOR_VALUECLASSS_IN_ARRAY 0xffff

/*****************************************************************************************/
LPCUTF8 ArrayMethodDesc::GetMethodName()
{
    LIMITED_METHOD_DAC_CONTRACT;

    switch (GetArrayFuncIndex())
    {
    case ARRAY_FUNC_GET:
        return "Get";
    case ARRAY_FUNC_SET:
        return "Set";
    case ARRAY_FUNC_ADDRESS:
        return "Address";
    default:
        return COR_CTOR_METHOD_NAME;    // ".ctor"
    }
}

/*****************************************************************************************/
DWORD ArrayMethodDesc::GetAttrs()
{
    LIMITED_METHOD_CONTRACT;
    return (GetArrayFuncIndex() >= ARRAY_FUNC_CTOR) ? (mdPublic | mdRTSpecialName) : mdPublic;
}

#ifndef DACCESS_COMPILE

/*****************************************************************************************/

//
// Generate a short sig (descr) for an array accessors
//

VOID ArrayClass::GenerateArrayAccessorCallSig(
    DWORD   dwRank,
    DWORD   dwFuncType,    // Load, store, or <init>
    PCCOR_SIGNATURE *ppSig,// Generated signature
    DWORD * pcSig,         // Generated signature size
    LoaderAllocator *pLoaderAllocator,
    AllocMemTracker *pamTracker,
    BOOL fForStubAsIL
    )
{
    CONTRACTL {
        STANDARD_VM_CHECK;
        PRECONDITION(dwRank >= 1 && dwRank < 0x3ffff);
    } CONTRACTL_END;

    PCOR_SIGNATURE pSig;
    PCOR_SIGNATURE pSigMemory;
    DWORD   dwCallSigSize = dwRank;
    DWORD   dwArgCount = (dwFuncType == ArrayMethodDesc::ARRAY_FUNC_SET) ? dwRank+1 : dwRank;
    DWORD   i;

    switch (dwFuncType)
    {
        // <callconv> <argcount> VAR 0 I4 , ... , I4
        case ArrayMethodDesc::ARRAY_FUNC_GET:
            dwCallSigSize += 4;
            break;

        // <callconv> <argcount> VOID I4 , ... , I4
        case ArrayMethodDesc::ARRAY_FUNC_CTOR:
            dwCallSigSize += 3;
            break;

        // <callconv> <argcount> VOID I4 , ... , I4 VAR 0
        case ArrayMethodDesc::ARRAY_FUNC_SET:
            dwCallSigSize += 5;
            break;

        // <callconv> <argcount> BYREF VAR 0 I4 , ... , I4
        case ArrayMethodDesc::ARRAY_FUNC_ADDRESS:
            dwCallSigSize += 5;
            if(fForStubAsIL) {dwArgCount++; dwCallSigSize++;}
            break;
    }

    // If the argument count is larger than 127 then it will require 2 bytes for the encoding
    if (dwArgCount > 0x7f)
        dwCallSigSize++;

    pSigMemory = (PCOR_SIGNATURE)pamTracker->Track(pLoaderAllocator->GetHighFrequencyHeap()->AllocMem(S_SIZE_T(dwCallSigSize)));

    pSig = pSigMemory;
    BYTE callConv = IMAGE_CEE_CS_CALLCONV_DEFAULT + IMAGE_CEE_CS_CALLCONV_HASTHIS;

    if (dwFuncType == ArrayMethodDesc::ARRAY_FUNC_ADDRESS && !fForStubAsIL)
    {
        callConv |= CORINFO_CALLCONV_PARAMTYPE;     // Address routine needs special hidden arg
    }

    *pSig++ = callConv;
    pSig += CorSigCompressData(dwArgCount, pSig);   // Argument count
    switch (dwFuncType)
    {
        case ArrayMethodDesc::ARRAY_FUNC_GET:
            *pSig++ = ELEMENT_TYPE_VAR;
            *pSig++ = 0;        // variable 0
            break;
        case ArrayMethodDesc::ARRAY_FUNC_CTOR:
            *pSig++ = (BYTE) ELEMENT_TYPE_VOID;             // Return type
            break;
        case ArrayMethodDesc::ARRAY_FUNC_SET:
            *pSig++ = (BYTE) ELEMENT_TYPE_VOID;             // Return type
            break;
        case ArrayMethodDesc::ARRAY_FUNC_ADDRESS:
            *pSig++ = (BYTE) ELEMENT_TYPE_BYREF;            // Return type
            *pSig++ = ELEMENT_TYPE_VAR;
            *pSig++ = 0;        // variable 0
            break;
    }

#ifndef TARGET_X86
    if(dwFuncType == ArrayMethodDesc::ARRAY_FUNC_ADDRESS && fForStubAsIL)
    {
        *pSig++ = ELEMENT_TYPE_I;
    }
#endif

    for (i = 0; i < dwRank; i++)
        *pSig++ = ELEMENT_TYPE_I4;

    if (dwFuncType == ArrayMethodDesc::ARRAY_FUNC_SET)
    {
        *pSig++ = ELEMENT_TYPE_VAR;
        *pSig++ = 0;        // variable 0
    }
#ifdef TARGET_X86
    else if(dwFuncType == ArrayMethodDesc::ARRAY_FUNC_ADDRESS && fForStubAsIL)
    {
        *pSig++ = ELEMENT_TYPE_I;
    }
#endif

    // Make sure the sig came out exactly as large as we expected
    _ASSERTE(pSig == pSigMemory + dwCallSigSize);

    *ppSig = pSigMemory;
    *pcSig = (DWORD)(pSig-pSigMemory);
}

//
// Allocate a new MethodDesc for a fake array method.
//
// Based on code in class.cpp.
//
void ArrayClass::InitArrayMethodDesc(
    ArrayMethodDesc *pNewMD,
    PCCOR_SIGNATURE pShortSig,
    DWORD   cShortSig,
    DWORD   dwVtableSlot,
    AllocMemTracker *pamTracker)
{
    STANDARD_VM_CONTRACT;

    // Note: The method desc memory is zero initialized

    pNewMD->SetMemberDef(0);

    pNewMD->SetSlot((WORD) dwVtableSlot);
    pNewMD->SetStoredMethodSig(pShortSig, cShortSig);

    _ASSERTE(!pNewMD->MayHaveNativeCode());
    pNewMD->SetTemporaryEntryPoint(pamTracker);

#ifdef _DEBUG
    _ASSERTE(pNewMD->GetMethodName() && GetDebugClassName());
    pNewMD->m_pszDebugMethodName = pNewMD->GetMethodName();
    pNewMD->m_pszDebugClassName  = GetDebugClassName();
    pNewMD->m_pDebugMethodTable = pNewMD->GetMethodTable();
#endif // _DEBUG
}

/*****************************************************************************************/
MethodTable* Module::CreateArrayMethodTable(TypeHandle elemTypeHnd, CorElementType arrayKind, unsigned Rank, AllocMemTracker *pamTracker)
{
    CONTRACTL {
        STANDARD_VM_CHECK;
        PRECONDITION(Rank > 0);
    } CONTRACTL_END;

    MethodTable * pElemMT = elemTypeHnd.GetMethodTable();

    CorElementType elemType = elemTypeHnd.GetSignatureCorElementType();

    // Shared EEClass if there is one
    MethodTable * pCanonMT = NULL;


    // Arrays of reference types all share the same EEClass.
    //
    // We can't share nested SZARRAYs because they have different
    // numbers of constructors.
    //
    // Unfortunately, we cannot share more because of it would affect user visible System.RuntimeMethodHandle behavior
    if (CorTypeInfo::IsObjRef(elemType) && elemType != ELEMENT_TYPE_SZARRAY && pElemMT != g_pObjectClass)
    {
        // This is loading the canonical version of the array so we can override
        OVERRIDE_TYPE_LOAD_LEVEL_LIMIT(CLASS_LOADED);
        pCanonMT = ClassLoader::LoadArrayTypeThrowing(TypeHandle(g_pObjectClass), arrayKind, Rank).AsMethodTable();
    }

    BOOL            containsPointers = CorTypeInfo::IsObjRef(elemType);
    if (elemType == ELEMENT_TYPE_VALUETYPE && pElemMT->ContainsGCPointers())
        containsPointers = TRUE;

    // this is the base for every array type
    MethodTable *pParentClass = g_pArrayClass;
    _ASSERTE(pParentClass);        // Must have already loaded the System.Array class
    _ASSERTE(pParentClass->IsFullyLoaded());

    DWORD numCtors = 2;         // ELEMENT_TYPE_ARRAY has two ctor functions, one with and one without lower bounds
    if (arrayKind == ELEMENT_TYPE_SZARRAY)
    {
        numCtors = 1;
        TypeHandle ptr = elemTypeHnd;
        while (ptr.GetInternalCorElementType() == ELEMENT_TYPE_SZARRAY) {
            numCtors++;
            ptr = ptr.GetArrayElementTypeHandle();
        }
    }

    /****************************************************************************************/

    // Parent class is the top level array
    // The vtable will have all of top level class's methods, plus any methods we have for array classes
    DWORD numVirtuals = pParentClass->GetNumVirtuals();
    DWORD numNonVirtualSlots = (pCanonMT == NULL) ? numCtors + 3: 0; // 3 for the proper rank Get, Set, Address

    size_t cbMT = sizeof(MethodTable);
    cbMT += MethodTable::GetNumVtableIndirections(numVirtuals) * sizeof(MethodTable::VTableIndir_t);

    // GC info
    size_t cbCGCDescData = 0;
    if (containsPointers)
    {
        cbCGCDescData += CGCDesc::ComputeSize(1);
        if (elemType == ELEMENT_TYPE_VALUETYPE)
        {
            size_t nSeries = CGCDesc::GetCGCDescFromMT(pElemMT)->GetNumSeries();
            cbCGCDescData += (nSeries - 1)*sizeof (val_serie_item);
            _ASSERTE(cbCGCDescData == CGCDesc::ComputeSizeRepeating(nSeries));
        }
    }

    // Allocate space for optional members
    // We always have a non-virtual slot array, see assert at end
    cbMT += MethodTable::GetOptionalMembersAllocationSize(true /* hasInterfaceMap */);

    // This is the offset of the beginning of the interface map
    size_t imapOffset = cbMT;

    // This is added after we determine the offset of the interface maps
    // because the memory appears before the pointer to the method table
    cbMT += cbCGCDescData;

    // Inherit top level class's interface map
    cbMT += pParentClass->GetNumInterfaces() * sizeof(InterfaceInfo_t);


    size_t cbArrayClass = 0;

    if (pCanonMT == NULL)
    {
        // Allocate ArrayClass, MethodTable, and class name in one alloc.
        // Remember to pad allocation size for ArrayClass portion to ensure MethodTable is pointer aligned.
        cbArrayClass = ALIGN_UP(sizeof(ArrayClass), sizeof(void*));
    }

    // ArrayClass already includes one void*
    LoaderAllocator* pAllocator= this->GetLoaderAllocator();
    BYTE* pMemory = (BYTE *)pamTracker->Track(pAllocator->GetHighFrequencyHeap()->AllocMem(S_SIZE_T(cbArrayClass) +
                                                                                            S_SIZE_T(cbMT)));

    // Note: Memory allocated on loader heap is zero filled
    // memset(pMemory, 0, sizeof(ArrayClass) + cbMT);

    ArrayClass* pClass = NULL;

    if (pCanonMT == NULL)
    {
        pClass = ::new (pMemory) ArrayClass();
    }

    // Head of MethodTable memory (starts after ArrayClass), this points at the GCDesc stuff in front
    // of a method table (if needed)
    BYTE* pMTHead = pMemory + cbArrayClass + cbCGCDescData;

    MethodTable* pMT = (MethodTable *) pMTHead;

    // Allocate the private data block ("private" during runtime in the ngen'ed case).
    pMT->AllocateAuxiliaryData(pAllocator, this, pamTracker, MethodTableStaticsFlags::None, static_cast<WORD>(numNonVirtualSlots));
    pMT->SetLoaderAllocator(pAllocator);
    pMT->SetModule(elemTypeHnd.GetModule());

    pMT->GetAuxiliaryDataForWrite()->SetIsNotFullyLoadedForBuildMethodTable();

    // Fill in pClass
    if (pClass != NULL)
    {
        pClass->SetInternalCorElementType(arrayKind);
        pClass->SetAttrClass (tdPublic | tdSerializable | tdSealed);  // This class is public, serializable, sealed
        pClass->SetRank (Rank);
        pClass->SetMethodTable (pMT);

        // Fill In the method table
        pClass->SetNumMethods(static_cast<WORD>(numVirtuals + numNonVirtualSlots));

        pClass->SetNumNonVirtualSlots(static_cast<WORD>(numNonVirtualSlots));
    }

    pMT->SetNumVirtuals(static_cast<WORD>(numVirtuals));

    pMT->SetParentMethodTable(pParentClass);

    // Method tables for arrays of generic type parameters are needed for type analysis.
    // No instances will be created, so we can use 0 as element size.
    DWORD dwComponentSize = CorTypeInfo::IsGenericVariable(elemType) ?
                                0 :
                                elemTypeHnd.GetSize();

    if (elemType == ELEMENT_TYPE_VALUETYPE || elemType == ELEMENT_TYPE_VOID)
    {
        // The only way for dwComponentSize to be large is to be part of a value class. If this changes
        // then the check will need to be moved outside valueclass check.
        if(dwComponentSize > MAX_SIZE_FOR_VALUECLASS_IN_ARRAY) {
            StackSString ssElemName;
            elemTypeHnd.GetName(ssElemName);

            elemTypeHnd.GetAssembly()->ThrowTypeLoadException(ssElemName.GetUTF8(), IDS_CLASSLOAD_VALUECLASSTOOLARGE);
        }
    }

    if (pClass != NULL)
    {
        pMT->SetClass(pClass);
    }
    else
    {
        pMT->SetCanonicalMethodTable(pCanonMT);
    }

    pMT->SetIsArray(arrayKind);

    pMT->SetArrayElementTypeHandle(elemTypeHnd);

    _ASSERTE(FitsIn<WORD>(dwComponentSize));
    pMT->SetComponentSize(static_cast<WORD>(dwComponentSize));

    pMT->SetModule(elemTypeHnd.GetModule());

    if (elemTypeHnd.ContainsGenericVariables())
        pMT->SetContainsGenericVariables();

#ifdef FEATURE_TYPEEQUIVALENCE
    if (elemTypeHnd.HasTypeEquivalence())
    {
        // propagate the type equivalence flag
        pMT->SetHasTypeEquivalence();
    }
#endif // FEATURE_TYPEEQUIVALENCE

    pMT->SetClassInited();

    // Set BaseSize to be size of non-data portion of the array
    DWORD baseSize = ARRAYBASE_BASESIZE;
    if (arrayKind == ELEMENT_TYPE_ARRAY)
        baseSize += Rank*sizeof(DWORD)*2;

#if !defined(TARGET_64BIT) && (DATA_ALIGNMENT > 4)
    if (dwComponentSize >= DATA_ALIGNMENT)
        baseSize = (DWORD)ALIGN_UP(baseSize, DATA_ALIGNMENT);
#endif // !defined(TARGET_64BIT) && (DATA_ALIGNMENT > 4)
    pMT->SetBaseSize(baseSize);
    // Because of array method table persisting, we need to copy the map
    for (unsigned index = 0; index < pParentClass->GetNumInterfaces(); ++index)
    {
      InterfaceInfo_t *pIntInfo = (InterfaceInfo_t *) (pMTHead + imapOffset + index * sizeof(InterfaceInfo_t));
      pIntInfo->SetMethodTable((pParentClass->GetInterfaceMap() + index)->GetMethodTable());
    }
    pMT->SetInterfaceMap((WORD)pParentClass->GetNumInterfaces(), (InterfaceInfo_t *)(pMTHead + imapOffset));

    // Copy down flags for these interfaces as well. This is simplified a bit since we know that System.Array
    // only has a few interfaces and the flags will fit inline into the MethodTable's optional members.
    _ASSERTE(MethodTable::GetExtraInterfaceInfoSize(pParentClass->GetNumInterfaces()) == 0);
    pMT->InitializeExtraInterfaceInfo(NULL);

    for (UINT32 i = 0; i < pParentClass->GetNumInterfaces(); i++)
    {
        if (pParentClass->IsInterfaceDeclaredOnClass(i))
            pMT->SetInterfaceDeclaredOnClass(i);
    }

    // The type is sufficiently initialized for most general purpose accessor methods to work.
    // Mark the type as restored to avoid asserts.
    pMT->GetAuxiliaryDataForWrite()->SetIsRestoredForBuildArrayMethodTable();

    {
        // Fill out the vtable indirection slots
        MethodTable::VtableIndirectionSlotIterator it = pMT->IterateVtableIndirectionSlots();
        while (it.Next())
        {
            // Share the parent chunk
            it.SetIndirectionSlot(pParentClass->GetVtableIndirections()[it.GetIndex()]);
        }
    }

#ifdef _DEBUG
    StackSString debugName;
    TypeString::AppendType(debugName, TypeHandle(pMT));
    const char* pDebugNameUTF8 = debugName.GetUTF8();
    S_SIZE_T safeLen = S_SIZE_T(strlen(pDebugNameUTF8))+S_SIZE_T(1);
    if(safeLen.IsOverflow()) COMPlusThrowHR(COR_E_OVERFLOW);
    size_t len = safeLen.Value();
    char * name = (char*) pamTracker->Track(pAllocator->
                                GetHighFrequencyHeap()->
                                AllocMem(safeLen));
    strcpy_s(name, len, pDebugNameUTF8);

    if (pClass != NULL)
        pClass->SetDebugClassName(name);
    pMT->SetDebugClassName(name);
#endif // _DEBUG

    if (pClass != NULL)
    {
        // Count the number of method descs we need so we can allocate chunks.
        DWORD dwMethodDescs = numCtors
                            + 3;        // for rank specific Get, Set, Address

        MethodDescChunk * pChunks = MethodDescChunk::CreateChunk(pAllocator->GetHighFrequencyHeap(),
                            dwMethodDescs, mcArray, FALSE /* fNonVtableSlot*/, FALSE /* fNativeCodeSlot */, FALSE /* HasAsyncMethodData */,
                            pMT, pamTracker);
        pClass->SetChunks(pChunks);

        MethodTable::IntroducedMethodIterator it(pMT);

        DWORD dwMethodIndex = 0;
        for (; it.IsValid(); it.Next())
        {
            ArrayMethodDesc* pNewMD = (ArrayMethodDesc *) it.GetMethodDesc();
            _ASSERTE(pNewMD->GetClassification() == mcArray);

            DWORD dwFuncRank;
            DWORD dwFuncType;

            if (dwMethodIndex < ArrayMethodDesc::ARRAY_FUNC_CTOR)
            {
                // Generate a new stand-alone, Rank Specific Get, Set and Address method.
                dwFuncRank = Rank;
                dwFuncType = dwMethodIndex;
            }
            else
            {
                if (arrayKind == ELEMENT_TYPE_SZARRAY)
                {
                    // For SZARRAY arrays, set up multiple constructors.
                    dwFuncRank = 1 + (dwMethodIndex - ArrayMethodDesc::ARRAY_FUNC_CTOR);
                }
                else
                {
                    // ELEMENT_TYPE_ARRAY has two constructors, one without lower bounds and one with lower bounds
                    _ASSERTE((dwMethodIndex == ArrayMethodDesc::ARRAY_FUNC_CTOR) || (dwMethodIndex == ArrayMethodDesc::ARRAY_FUNC_CTOR+1));
                    dwFuncRank = (dwMethodIndex == ArrayMethodDesc::ARRAY_FUNC_CTOR) ? Rank : 2 * Rank;
                }
                dwFuncType = ArrayMethodDesc::ARRAY_FUNC_CTOR;
            }

            PCCOR_SIGNATURE pSig;
            DWORD           cSig;

            pClass->GenerateArrayAccessorCallSig(dwFuncRank, dwFuncType, &pSig, &cSig, pAllocator, pamTracker, FALSE);

            pClass->InitArrayMethodDesc(pNewMD, pSig, cSig, numVirtuals + dwMethodIndex, pamTracker);

            dwMethodIndex++;
        }
        _ASSERTE(dwMethodIndex == dwMethodDescs);
    }

    // Set up GC information
    if (CorTypeInfo::IsObjRef(elemType) ||
        ((elemType == ELEMENT_TYPE_VALUETYPE) && pElemMT->IsAllGCPointers()))
    {
        pMT->SetContainsGCPointers();

        // This array is all GC Pointers
        CGCDesc::GetCGCDescFromMT(pMT)->Init( pMT, 1 );

        CGCDescSeries* pSeries = CGCDesc::GetCGCDescFromMT(pMT)->GetHighestSeries();

        int offsetToData = ArrayBase::GetDataPtrOffset(pMT);
        // For arrays, the size is the negative of the BaseSize (the GC always adds the total
        // size of the object, so what you end up with is the size of the data portion of the array)
        pSeries->SetSeriesSize(-(SSIZE_T)(offsetToData + sizeof(size_t)));
        pSeries->SetSeriesOffset(offsetToData);
    }
    else if (elemType == ELEMENT_TYPE_VALUETYPE)
    {
        // If it's an array of value classes, there is a different format for the GCDesc if it contains pointers
        if (pElemMT->ContainsGCPointers())
        {
            pMT->SetContainsGCPointers();

            CGCDescSeries* pElemSeries = CGCDesc::GetCGCDescFromMT(pElemMT)->GetHighestSeries();

            // negative series has a special meaning, indicating a different form of GCDesc
            SSIZE_T nSeries = (SSIZE_T) CGCDesc::GetCGCDescFromMT(pElemMT)->GetNumSeries();
            CGCDesc::GetCGCDescFromMT(pMT)->InitValueClassSeries(pMT, nSeries);

            CGCDescSeries* pSeries = CGCDesc::GetCGCDescFromMT(pMT)->GetHighestSeries();

#if _DEBUG
            // GC series must be sorted by the offset
            // we will validate that here just in case.
            size_t prevOffset = pElemSeries[0].GetSeriesOffset();
            for (int index = 1; index < nSeries; index++)
            {
                size_t offset = pElemSeries[-index].GetSeriesOffset();
                _ASSERTE((offset - prevOffset) > 0);
                prevOffset = offset;
            }
#endif // _DEBUG

            // Offset of the first pointer in the array
            // This equals the offset of the first pointer if this were an array of entirely pointers, plus the offset of the
            // first pointer in the value class
            pSeries->SetSeriesOffset(ArrayBase::GetDataPtrOffset(pMT)
                + (pElemSeries[0].GetSeriesOffset()) - OBJECT_SIZE);

            for (int index = 0; index < nSeries; index ++)
            {
                size_t numPtrsInBytes = pElemSeries[-index].GetSeriesSize()
                    + pElemMT->GetBaseSize();
                size_t currentOffset;
                size_t skip;
                currentOffset = pElemSeries[-index].GetSeriesOffset()+numPtrsInBytes;
                if (index != nSeries-1)
                {
                    skip = pElemSeries[-(index+1)].GetSeriesOffset()-currentOffset;
                }
                else if (index == 0)
                {
                    skip = pElemMT->GetNumInstanceFieldBytes() - numPtrsInBytes;
                }
                else
                {
                    skip = pElemSeries[0].GetSeriesOffset() + pElemMT->GetBaseSize()
                         - OBJECT_BASESIZE - currentOffset;
                }

                _ASSERTE(!"Module::CreateArrayMethodTable() - unaligned GC info" || IS_ALIGNED(skip, TARGET_POINTER_SIZE));

                unsigned short NumPtrs = (unsigned short) (numPtrsInBytes / TARGET_POINTER_SIZE);
                if(skip > MAX_SIZE_FOR_VALUECLASS_IN_ARRAY || numPtrsInBytes > MAX_PTRS_FOR_VALUECLASSS_IN_ARRAY) {
                    StackSString ssElemName;
                    elemTypeHnd.GetName(ssElemName);

                    elemTypeHnd.GetAssembly()->ThrowTypeLoadException(ssElemName.GetUTF8(),
                                                                      IDS_CLASSLOAD_VALUECLASSTOOLARGE);
                }

                // pSeries->val_serie is a fixed sized array.
                // Use pointer arithmetic instead of direct array access to avoid compilers, specifically GCC,
                // to discover undefined behavior and generate unintended code when optimization is turned on.
                val_serie_item *val_item = pSeries->val_serie - index;

                val_item->set_val_serie_item (NumPtrs, (unsigned short)skip);
            }
        }
    }

    // If we get here we are assuming that there was no truncation. If this is not the case then
    // an array whose base type is not a value class was created and was larger then 0xffff (a word)
    _ASSERTE(dwComponentSize == pMT->GetComponentSize());

    return(pMT);
} // Module::CreateArrayMethodTable


class ArrayOpLinker : public ILStubLinker
{
    ILCodeStream * m_pCode;
    ArrayMethodDesc * m_pMD;

    SigTypeContext m_emptyContext;

public:
    ArrayOpLinker(ArrayMethodDesc * pMD)
        : ILStubLinker(pMD->GetModule(), pMD->GetSignature(), &m_emptyContext, pMD, (ILStubLinkerFlags)(ILSTUB_LINKER_FLAG_STUB_HAS_THIS | ILSTUB_LINKER_FLAG_TARGET_HAS_THIS))
    {
        m_pCode = NewCodeStream(kDispatch);
        m_pMD = pMD;
    }

    void EmitStub()
    {
        MethodTable *pMT = m_pMD->GetMethodTable();
        BOOL fHasLowerBounds = pMT->GetInternalCorElementType() == ELEMENT_TYPE_ARRAY;

        DWORD dwTotalLocalNum = NewLocal(ELEMENT_TYPE_I4);
        DWORD dwLengthLocalNum = NewLocal(ELEMENT_TYPE_I4);

        mdToken tokRawData = GetToken(CoreLibBinder::GetField(FIELD__RAW_DATA__DATA));

        ILCodeLabel * pRangeExceptionLabel = NewCodeLabel();
        ILCodeLabel * pRangeExceptionLabel1 = NewCodeLabel();
        ILCodeLabel * pCheckDone = NewCodeLabel();
        ILCodeLabel * pNotSZArray = NewCodeLabel();
        ILCodeLabel * pTypeMismatchExceptionLabel = NULL;

        UINT rank = pMT->GetRank();
        UINT firstIdx = 0;
        UINT hiddenArgIdx = rank;
        _ASSERTE(rank>0);

#ifndef TARGET_X86
        if(m_pMD->GetArrayFuncIndex() == ArrayMethodDesc::ARRAY_FUNC_ADDRESS)
        {
            firstIdx = 1;
            hiddenArgIdx = 0;
        }
#endif
        CorElementType sigElementType = pMT->GetArrayElementType();
        if (CorTypeInfo::IsObjRef(sigElementType))
        {
            // Type Check
            if(m_pMD->GetArrayFuncIndex() == ArrayMethodDesc::ARRAY_FUNC_SET)
            {
                ILCodeLabel * pTypeCheckOK = NewCodeLabel();

                m_pCode->EmitLDARG(rank); // load value to store
                m_pCode->EmitBRFALSE(pTypeCheckOK); //Storing NULL is OK

                m_pCode->EmitLDARG(rank); // return param
                m_pCode->EmitLDFLDA(tokRawData);
                m_pCode->EmitLDC(Object::GetOffsetOfFirstField());
                m_pCode->EmitSUB();
                m_pCode->EmitLDIND_I(); // TypeHandle

                m_pCode->EmitLoadThis();
                m_pCode->EmitLDFLDA(tokRawData);
                m_pCode->EmitLDC(Object::GetOffsetOfFirstField());
                m_pCode->EmitSUB();
                m_pCode->EmitLDIND_I(); // Array MT
                m_pCode->EmitLDC(MethodTable::GetOffsetOfArrayElementTypeHandle());
                m_pCode->EmitADD();
                m_pCode->EmitLDIND_I();

                m_pCode->EmitCEQ();
                m_pCode->EmitBRTRUE(pTypeCheckOK); // Same type is OK

                // Call type check helper
                m_pCode->EmitLDARG(rank);
                m_pCode->EmitLoadThis();
                m_pCode->EmitCALL(METHOD__CASTHELPERS__ARRAYTYPECHECK,2,0);

                m_pCode->EmitLabel(pTypeCheckOK);

            }
            else if(m_pMD->GetArrayFuncIndex() == ArrayMethodDesc::ARRAY_FUNC_ADDRESS)
            {
                // Check that the hidden param is same type
                ILCodeLabel *pTypeCheckPassed = NewCodeLabel();
                pTypeMismatchExceptionLabel = NewCodeLabel();

                m_pCode->EmitLDARG(hiddenArgIdx); // hidden param
                m_pCode->EmitBRFALSE(pTypeCheckPassed);
                m_pCode->EmitLDARG(hiddenArgIdx);

                m_pCode->EmitLoadThis();
                m_pCode->EmitLDFLDA(tokRawData);
                m_pCode->EmitLDC(Object::GetOffsetOfFirstField());
                m_pCode->EmitSUB();
                m_pCode->EmitLDIND_I(); // Array MT

                m_pCode->EmitCEQ();
                m_pCode->EmitBRFALSE(pTypeMismatchExceptionLabel); // throw exception if not same
                m_pCode->EmitLabel(pTypeCheckPassed);
            }
        }

        if(rank == 1 && fHasLowerBounds)
        {
            // check if the array is SZArray.
            m_pCode->EmitLoadThis();
            m_pCode->EmitLDFLDA(tokRawData);
            m_pCode->EmitLDC(Object::GetOffsetOfFirstField());
            m_pCode->EmitSUB();
            m_pCode->EmitLDIND_I();
            m_pCode->EmitLDC(MethodTable::GetOffsetOfFlags());
            m_pCode->EmitADD();
            m_pCode->EmitLDIND_I4();
            m_pCode->EmitLDC(MethodTable::GetIfArrayThenSzArrayFlag());
            m_pCode->EmitAND();
            m_pCode->EmitBRFALSE(pNotSZArray); // goto multi-dimmArray code if not szarray

            // it is SZArray
            // bounds check
            m_pCode->EmitLoadThis();
            m_pCode->EmitLDFLDA(tokRawData);
            m_pCode->EmitLDC(ArrayBase::GetOffsetOfNumComponents() - Object::GetOffsetOfFirstField());
            m_pCode->EmitADD();
            m_pCode->EmitLDIND_I4();
            m_pCode->EmitLDARG(firstIdx);
            m_pCode->EmitBLE_UN(pRangeExceptionLabel);

            m_pCode->EmitLoadThis();
            m_pCode->EmitLDFLDA(tokRawData);
            m_pCode->EmitLDC(ArrayBase::GetBoundsOffset(pMT) - Object::GetOffsetOfFirstField());
            m_pCode->EmitADD();
            m_pCode->EmitLDARG(firstIdx);
            m_pCode->EmitBR(pCheckDone);
            m_pCode->EmitLabel(pNotSZArray);
        }

        for (UINT i = 0; i < rank; i++)
        {
            // Cache length
            m_pCode->EmitLoadThis();
            m_pCode->EmitLDFLDA(tokRawData);
            m_pCode->EmitLDC((ArrayBase::GetBoundsOffset(pMT) - Object::GetOffsetOfFirstField()) + i*sizeof(DWORD));
            m_pCode->EmitADD();
            m_pCode->EmitLDIND_I4();
            m_pCode->EmitSTLOC(dwLengthLocalNum);

            // Fetch index
            m_pCode->EmitLDARG(firstIdx + i);

            if (fHasLowerBounds)
            {
                // Load lower bound
                m_pCode->EmitLoadThis();
                m_pCode->EmitLDFLDA(tokRawData);
                m_pCode->EmitLDC((ArrayBase::GetLowerBoundsOffset(pMT) - Object::GetOffsetOfFirstField()) + i*sizeof(DWORD));
                m_pCode->EmitADD();
                m_pCode->EmitLDIND_I4();

                // Subtract lower bound
                m_pCode->EmitSUB();
            }

            // Compare with length
            m_pCode->EmitDUP();
            m_pCode->EmitLDLOC(dwLengthLocalNum);
            m_pCode->EmitBGE_UN(pRangeExceptionLabel1);

            // Add to the running total if we have one already
            if (i > 0)
            {
                m_pCode->EmitLDLOC(dwTotalLocalNum);
                m_pCode->EmitLDLOC(dwLengthLocalNum);
                m_pCode->EmitMUL();
                m_pCode->EmitADD();
            }
            m_pCode->EmitSTLOC(dwTotalLocalNum);
        }

        // Compute element address
        m_pCode->EmitLoadThis();
        m_pCode->EmitLDFLDA(tokRawData);
        m_pCode->EmitLDC(ArrayBase::GetDataPtrOffset(pMT) - Object::GetOffsetOfFirstField());
        m_pCode->EmitADD();
        m_pCode->EmitLDLOC(dwTotalLocalNum);

        m_pCode->EmitLabel(pCheckDone);

        m_pCode->EmitCONV_U();

        SIZE_T elemSize = pMT->GetComponentSize();
        if (elemSize != 1)
        {
            m_pCode->EmitLDC(elemSize);
            m_pCode->EmitMUL();
        }
        m_pCode->EmitADD();

        LocalDesc elemType(pMT->GetArrayElementTypeHandle().GetInternalCorElementType());

        switch (m_pMD->GetArrayFuncIndex())
        {

        case ArrayMethodDesc::ARRAY_FUNC_GET:
            if(elemType.ElementType[0]==ELEMENT_TYPE_VALUETYPE)
            {
                m_pCode->EmitLDOBJ(GetToken(pMT->GetArrayElementTypeHandle()));
            }
            else
                m_pCode->EmitLDIND_T(&elemType);
            break;

        case ArrayMethodDesc::ARRAY_FUNC_SET:
            // Value to store into the array
            m_pCode->EmitLDARG(rank);

            if(elemType.ElementType[0]==ELEMENT_TYPE_VALUETYPE)
            {
                m_pCode->EmitSTOBJ(GetToken(pMT->GetArrayElementTypeHandle()));
            }
            else
                m_pCode->EmitSTIND_T(&elemType);
            break;

        case ArrayMethodDesc::ARRAY_FUNC_ADDRESS:
            break;

        default:
            _ASSERTE(!"Unknown ArrayFuncIndex");
        }

        m_pCode->EmitRET();

        m_pCode->EmitLDC(0);
        m_pCode->EmitLabel(pRangeExceptionLabel1); // Assumes that there is one "int" pushed on the stack
        m_pCode->EmitPOP();

        mdToken tokIndexOutOfRangeCtorExcep = GetToken((CoreLibBinder::GetException(kIndexOutOfRangeException))->GetDefaultConstructor());
        m_pCode->EmitLabel(pRangeExceptionLabel);
        m_pCode->EmitNEWOBJ(tokIndexOutOfRangeCtorExcep, 0);
        m_pCode->EmitTHROW();

        if(pTypeMismatchExceptionLabel != NULL)
        {
            mdToken tokTypeMismatchExcepCtor = GetToken((CoreLibBinder::GetException(kArrayTypeMismatchException))->GetDefaultConstructor());

            m_pCode->EmitLabel(pTypeMismatchExceptionLabel);
            m_pCode->EmitNEWOBJ(tokTypeMismatchExcepCtor, 0);
            m_pCode->EmitTHROW();
        }
    }
};

Stub *GenerateArrayOpStub(ArrayMethodDesc* pMD)
{
    STANDARD_VM_CONTRACT;

    ArrayOpLinker sl(pMD);

    sl.EmitStub();

    PCCOR_SIGNATURE pSig;
    DWORD cbSig;
    AllocMemTracker amTracker;

    if (pMD->GetArrayFuncIndex() == ArrayMethodDesc::ARRAY_FUNC_ADDRESS)
    {
         // The stub has to have signature with explicit hidden argument instead of CORINFO_CALLCONV_PARAMTYPE.
         // Generate a new signature for the stub here.
         ((ArrayClass*)(pMD->GetMethodTable()->GetClass()))->GenerateArrayAccessorCallSig(pMD->GetMethodTable()->GetRank(),
                                                                                          ArrayMethodDesc::ARRAY_FUNC_ADDRESS,
                                                                                          &pSig,
                                                                                          &cbSig,
                                                                                          pMD->GetLoaderAllocator(),
                                                                                          &amTracker,
                                                                                          1);
    }
    else
    {
         pMD->GetSig(&pSig,&cbSig);
    }

    amTracker.SuppressRelease();

    static const ILStubTypes stubTypes[3] = { ILSTUB_ARRAYOP_GET, ILSTUB_ARRAYOP_SET, ILSTUB_ARRAYOP_ADDRESS };

    _ASSERTE(pMD->GetArrayFuncIndex() <= ARRAY_SIZE(stubTypes));
    PInvokeStubFlags arrayOpStubFlag = (PInvokeStubFlags)stubTypes[pMD->GetArrayFuncIndex()];

    MethodDesc * pStubMD = ILStubCache::CreateAndLinkNewILStubMethodDesc(pMD->GetLoaderAllocator(),
                                                            pMD->GetMethodTable(),
                                                            arrayOpStubFlag,
                                                            pMD->GetModule(),
                                                            pSig, cbSig,
                                                            NULL,
                                                            &sl);

    return Stub::NewStub(JitILStub(pStubMD));
}


//---------------------------------------------------------------------
// This method returns TRUE if pInterfaceMT could be one of the interfaces
// that are implicitly implemented by SZArrays

BOOL IsImplicitInterfaceOfSZArray(MethodTable *pInterfaceMT)
{
    LIMITED_METHOD_CONTRACT;
    PRECONDITION(pInterfaceMT->IsInterface());
    PRECONDITION(pInterfaceMT->HasInstantiation());

    // Is target interface Anything<T> in CoreLib?
    if (!pInterfaceMT->HasInstantiation() || !pInterfaceMT->GetModule()->IsSystem())
        return FALSE;

    unsigned rid = pInterfaceMT->GetTypeDefRid();

    // Is target interface IList<T> or one of its ancestors, or IReadOnlyList<T>?
    return (rid == CoreLibBinder::GetExistingClass(CLASS__ILISTGENERIC)->GetTypeDefRid() ||
            rid == CoreLibBinder::GetExistingClass(CLASS__ICOLLECTIONGENERIC)->GetTypeDefRid() ||
            rid == CoreLibBinder::GetExistingClass(CLASS__IENUMERABLEGENERIC)->GetTypeDefRid() ||
            rid == CoreLibBinder::GetExistingClass(CLASS__IREADONLYCOLLECTIONGENERIC)->GetTypeDefRid() ||
            rid == CoreLibBinder::GetExistingClass(CLASS__IREADONLYLISTGENERIC)->GetTypeDefRid());
}

//----------------------------------------------------------------------------------
// Calls to (IList<T>)(array).Meth are actually implemented by SZArrayHelper.Meth<T>
// This workaround exists for two reasons:
//
//    - For working set reasons, we don't want insert these methods in the array hierarchy
//      in the normal way.
//    - For platform and devtime reasons, we still want to use the C# compiler to generate
//      the method bodies.
//
// (Though it's questionable whether any devtime was saved.)
//
// This method takes care of the mapping between the two. Give it a method
// IList<T>.Meth, and it will return SZArrayHelper.Meth<T>.
//----------------------------------------------------------------------------------
MethodDesc* GetActualImplementationForArrayGenericIListOrIReadOnlyListMethod(MethodDesc *pItfcMeth,  TypeHandle theT)
{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        INJECT_FAULT(COMPlusThrowOM());
    }
    CONTRACTL_END

    int slot = pItfcMeth->GetSlot();

    // We need to pick the right starting method depending on the depth of the inheritance chain
    static const BinderMethodID startingMethod[] = {
        METHOD__SZARRAYHELPER__GETENUMERATOR,   // First method of IEnumerable`1
        METHOD__SZARRAYHELPER__GET_COUNT,       // First method of ICollection`1/IReadOnlyCollection`1
        METHOD__SZARRAYHELPER__GET_ITEM         // First method of IList`1/IReadOnlyList`1
    };

    // Subtract one for the non-generic IEnumerable that the generic enumerable inherits from
    unsigned int inheritanceDepth = pItfcMeth->GetMethodTable()->GetNumInterfaces() - 1;
    _ASSERTE(0 <= inheritanceDepth && inheritanceDepth < ARRAY_SIZE(startingMethod));

    MethodDesc *pGenericImplementor = CoreLibBinder::GetMethod((BinderMethodID)(startingMethod[inheritanceDepth] + slot));

    // The most common reason for this assert is that the order of the SZArrayHelper methods in
    // corelib.h does not match the order they are implemented on the generic interfaces.
    _ASSERTE(pGenericImplementor == MemberLoader::FindMethodByName(g_pSZArrayHelperClass, pItfcMeth->GetName()));

    // OPTIMIZATION: For any method other than GetEnumerator(), we can safely substitute
    // "Object" for reference-type theT's. This causes fewer methods to be instantiated.
    if (startingMethod[inheritanceDepth] != METHOD__SZARRAYHELPER__GETENUMERATOR &&
        !theT.IsValueType())
    {
        theT = TypeHandle(g_pObjectClass);
    }

    MethodDesc *pActualImplementor = MethodDesc::FindOrCreateAssociatedMethodDesc(pGenericImplementor,
                                                                                  g_pSZArrayHelperClass,
                                                                                  FALSE,
                                                                                  Instantiation(&theT, 1),
                                                                                  FALSE // allowInstParam
                                                                                  );
    _ASSERTE(pActualImplementor);
    return pActualImplementor;
}
#endif // DACCESS_COMPILE

CorElementType GetNormalizedIntegralArrayElementType(CorElementType elementType)
{
    LIMITED_METHOD_CONTRACT;

    _ASSERTE(CorTypeInfo::IsPrimitiveType_NoThrow(elementType));

    // Array Primitive types such as E_T_I4 and E_T_U4 are interchangeable
    // Enums with interchangeable underlying types are interchangeable
    // BOOL is NOT interchangeable with I1/U1, neither CHAR -- with I2/U2

    switch (elementType)
    {
    case ELEMENT_TYPE_U1:
    case ELEMENT_TYPE_U2:
    case ELEMENT_TYPE_U4:
    case ELEMENT_TYPE_U8:
    case ELEMENT_TYPE_U:
        return (CorElementType)(elementType - 1); // normalize to signed type
    default:
        break;
    }

    return elementType;
}
