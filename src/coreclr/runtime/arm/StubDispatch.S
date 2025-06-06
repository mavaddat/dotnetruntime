// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

.syntax unified
.thumb

#include "AsmMacros_Shared.h"

#ifdef FEATURE_CACHED_INTERFACE_DISPATCH

// Macro that generates a stub consuming a cache with the given number of entries.
.macro DEFINE_INTERFACE_DISPATCH_STUB entries

NESTED_ENTRY RhpInterfaceDispatch\entries, _TEXT, NoHandler
        // r12 currently contains the indirection cell address. But we need more scratch registers and
        // we may A/V on a null this. Store r1 and r2 in red zone.
        str         r1, [sp, #-8]
        str         r2, [sp, #-4]

        // r12 currently holds the indirection cell address. We need to get the cache structure instead.
        ldr         r2, [r12, #OFFSETOF__InterfaceDispatchCell__m_pCache]

        // Load the MethodTable from the object instance in r0.
        GLOBAL_LABEL RhpInterfaceDispatchAVLocation\entries
        ldr         r1, [r0]

        CurrentOffset = OFFSETOF__InterfaceDispatchCache__m_rgEntries
        // For each entry in the cache, see if its MethodTable type matches the MethodTable in r1.
        // If so, call the second cache entry.  If not, skip the InterfaceDispatchCacheEntry.
        //  R1 : Instance MethodTable*
        //  R2: Cache data structure
        //  R12 : Trashed. On successful check, set to the target address to jump to.
        .rept \entries
              ldr        r12, [r2, #CurrentOffset]
              cmp        r1, r12
              bne        0f
              ldr        r12, [r2, #(CurrentOffset + 4)]
              b          LOCAL_LABEL(99_\entries)
        0:
              CurrentOffset = CurrentOffset + 8
        .endr

        // Point r12 to the indirection cell using the back pointer in the cache block
        ldr         r12, [r2, #OFFSETOF__InterfaceDispatchCache__m_pCell]

        ldr         r1, [sp, #-8]
        ldr         r2, [sp, #-4]
        b           C_FUNC(RhpInterfaceDispatchSlow)

        // Common epilog for cache hits. Have to out of line it here due to limitation on the number of
        // epilogs imposed by the unwind code macros.
LOCAL_LABEL(99_\entries):
        // R2 contains address of the cache block. We store it in the red zone in case the target we jump
        // to needs it.
        // R12 contains the target address to jump to
        ldr         r1, [sp, #-8]
        // We have to store R2 with address of the cache block into red zone before restoring original r2.
        str         r2, [sp, #-8]
        ldr         r2, [sp, #-4]
        EPILOG_BRANCH_REG r12

NESTED_END RhpInterfaceDispatch\entries, _TEXT

.endm // DEFINE_INTERFACE_DISPATCH_STUB

// Define all the stub routines we currently need.
//
// The mrt100dbi requires these be exported to identify mrt100 code that dispatches back into managed.
// If you change or add any new dispatch stubs, please also change slr.def and dbi\process.cpp CordbProcess::GetExportStepInfo
//
DEFINE_INTERFACE_DISPATCH_STUB 1
DEFINE_INTERFACE_DISPATCH_STUB 2
DEFINE_INTERFACE_DISPATCH_STUB 4
DEFINE_INTERFACE_DISPATCH_STUB 8
DEFINE_INTERFACE_DISPATCH_STUB 16
DEFINE_INTERFACE_DISPATCH_STUB 32
DEFINE_INTERFACE_DISPATCH_STUB 64

// Initial dispatch on an interface when we don't have a cache yet.
LEAF_ENTRY RhpInitialInterfaceDispatch, _TEXT
        // Just tail call to the cache miss helper.
        b           C_FUNC(RhpInterfaceDispatchSlow)
LEAF_END RhpInitialInterfaceDispatch, _TEXT

// No as alternate entry due to missed thumb bit in this case
// See https://github.com/dotnet/runtime/issues/8608
LEAF_ENTRY RhpInitialDynamicInterfaceDispatch, _TEXT
        // Just tail call to the cache miss helper.
        b           C_FUNC(RhpInterfaceDispatchSlow)
LEAF_END RhpInitialDynamicInterfaceDispatch, _TEXT

// Cache miss case, call the runtime to resolve the target and update the cache.
// Use universal transition helper to allow an exception to flow out of resolution
LEAF_ENTRY RhpInterfaceDispatchSlow, _TEXT
        // r12 has the interface dispatch cell address in it.
        // The calling convention of the universal thunk is that the parameter
        // for the universal thunk target is to be placed in sp-8
        // and the universal thunk target address is to be placed in sp-4
        str         r12, [sp, #-8]
        PREPARE_EXTERNAL_VAR RhpCidResolve, r12
        str         r12, [sp, #-4]

        // jump to universal transition thunk
        b           C_FUNC(RhpUniversalTransition_DebugStepTailCall)
LEAF_END RhpInterfaceDispatchSlow, _TEXT

#endif // FEATURE_CACHED_INTERFACE_DISPATCH
