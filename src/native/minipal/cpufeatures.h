// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#ifndef HAVE_MINIPAL_CPUFEATURES_H
#define HAVE_MINIPAL_CPUFEATURES_H

//
// Should match the constants defined in the compiler in HardwareIntrinsicHelpers.cs
//

#if defined(HOST_X86) || defined(HOST_AMD64)
enum XArchIntrinsicConstants
{
    XArchIntrinsicConstants_Aes = 0x0001,
    XArchIntrinsicConstants_Pclmulqdq = 0x0002,
    XArchIntrinsicConstants_Sse3 = 0x0004,
    XArchIntrinsicConstants_Ssse3 = 0x0008,
    XArchIntrinsicConstants_Sse41 = 0x0010,
    XArchIntrinsicConstants_Sse42 = 0x0020,
    XArchIntrinsicConstants_Popcnt = 0x0040,
    XArchIntrinsicConstants_Avx = 0x0080,
    XArchIntrinsicConstants_Fma = 0x0100,
    XArchIntrinsicConstants_Avx2 = 0x0200,
    XArchIntrinsicConstants_Bmi1 = 0x0400,
    XArchIntrinsicConstants_Bmi2 = 0x0800,
    XArchIntrinsicConstants_Lzcnt = 0x1000,
    XArchIntrinsicConstants_AvxVnni = 0x2000,
    XArchIntrinsicConstants_Movbe = 0x4000,
    XArchIntrinsicConstants_Avx512 = 0x8000,
    XArchIntrinsicConstants_Avx512Vbmi = 0x10000,
    XArchIntrinsicConstants_Serialize = 0x20000,
    XArchIntrinsicConstants_Avx10v1 = 0x40000,
    XArchIntrinsicConstants_Apx = 0x80000,
    XArchIntrinsicConstants_Vpclmulqdq = 0x100000,
    XArchIntrinsicConstants_Avx10v2 = 0x200000,
    XArchIntrinsicConstants_Gfni = 0x400000,
};
#endif // HOST_X86 || HOST_AMD64

#if defined(HOST_ARM64)
enum ARM64IntrinsicConstants
{
    ARM64IntrinsicConstants_Aes = 0x0001,
    ARM64IntrinsicConstants_Crc32 = 0x0002,
    ARM64IntrinsicConstants_Dp = 0x0004,
    ARM64IntrinsicConstants_Rdm = 0x0008,
    ARM64IntrinsicConstants_Sha1 = 0x0010,
    ARM64IntrinsicConstants_Sha256 = 0x0020,
    ARM64IntrinsicConstants_Atomics = 0x0040,
    ARM64IntrinsicConstants_Rcpc = 0x0080,
    ARM64IntrinsicConstants_Rcpc2 = 0x0100,
    ARM64IntrinsicConstants_Sve = 0x0200,
    ARM64IntrinsicConstants_Sve2 = 0x0400,
};

#include <assert.h>

// Bit position for the ARM64IntrinsicConstants_Atomics flags, to be used with tbz / tbnz instructions
#define ARM64_ATOMICS_FEATURE_FLAG_BIT 6
static_assert((1 << ARM64_ATOMICS_FEATURE_FLAG_BIT) == ARM64IntrinsicConstants_Atomics, "ARM64_ATOMICS_FEATURE_FLAG_BIT must match with ARM64IntrinsicConstants_Atomics");

#endif // HOST_ARM64

#if defined(HOST_RISCV64)
enum RiscV64IntrinsicConstants
{
    RiscV64IntrinsicConstants_Zba = 0x0001,
    RiscV64IntrinsicConstants_Zbb = 0x0002,
};
#endif // HOST_RISCV64

#ifdef __cplusplus
extern "C"
{
#endif // __cplusplus

int minipal_getcpufeatures(void);
bool minipal_detect_rosetta(void);

#ifdef __cplusplus
}
#endif // __cplusplus

#endif
