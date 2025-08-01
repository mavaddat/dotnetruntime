// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "pal_evp_pkey.h"
#include "pal_evp_pkey_slh_dsa.h"
#include "pal_evp_pkey_raw_signverify.h"
#include "pal_utilities.h"
#include "openssl.h"
#include <assert.h>

EVP_PKEY* CryptoNative_SlhDsaGenerateKey(const char* keyType)
{
#if defined(NEED_OPENSSL_3_0) && HAVE_OPENSSL_EVP_PKEY_SIGN_MESSAGE_INIT
    if (!API_EXISTS(EVP_PKEY_sign_message_init) ||
        !API_EXISTS(EVP_PKEY_verify_message_init))
    {
        return NULL;
    }

    ERR_clear_error();

    EVP_PKEY_CTX* pctx = EVP_PKEY_CTX_new_from_name(NULL, keyType, NULL);
    EVP_PKEY* pkey = NULL;

    if (!pctx)
    {
        return NULL;
    }

    if (EVP_PKEY_keygen_init(pctx) <= 0)
    {
        goto done;
    }

    if (EVP_PKEY_keygen(pctx, &pkey) != 1 && pkey != NULL)
    {
        EVP_PKEY_free(pkey);
        pkey = NULL;
    }

done:
    if (pctx != NULL)
    {
        EVP_PKEY_CTX_free(pctx);
    }

    return pkey;
#else
    (void)keyType;
    return NULL;
#endif
}

int32_t CryptoNative_SlhDsaSignPure(EVP_PKEY *pkey,
                                    void* extraHandle,
                                    uint8_t* msg, int32_t msgLen,
                                    uint8_t* context, int32_t contextLen,
                                    uint8_t* destination, int32_t destinationLen)
{
    assert(destinationLen >= 7856 /* SLH-DSA-SHA2-128s/SLH-DSA-SHAKE-128s signature size */);
    return CryptoNative_EvpPKeySignPure(pkey, extraHandle, msg, msgLen, context, contextLen, destination, destinationLen);
}

int32_t CryptoNative_SlhDsaVerifyPure(EVP_PKEY *pkey,
                                      void* extraHandle,
                                      uint8_t* msg, int32_t msgLen,
                                      uint8_t* context, int32_t contextLen,
                                      uint8_t* sig, int32_t sigLen)
{
    assert(sigLen >= 7856 /* SLH-DSA-SHA2-128s/SLH-DSA-SHAKE-128s signature size */);
    return CryptoNative_EvpPKeyVerifyPure(pkey, extraHandle, msg, msgLen, context, contextLen, sig, sigLen);
}

int32_t CryptoNative_SlhDsaSignPreEncoded(EVP_PKEY *pkey,
                                          void* extraHandle,
                                          uint8_t* msg, int32_t msgLen,
                                          uint8_t* destination, int32_t destinationLen)
{
    assert(destinationLen >= 7856 /* SLH-DSA-SHA2-128s/SLH-DSA-SHAKE-128s signature size */);
    return CryptoNative_EvpPKeySignPreEncoded(pkey, extraHandle, msg, msgLen, destination, destinationLen);
}

int32_t CryptoNative_SlhDsaVerifyPreEncoded(EVP_PKEY *pkey,
                                            void* extraHandle,
                                            uint8_t* msg, int32_t msgLen,
                                            uint8_t* sig, int32_t sigLen)
{
    assert(sigLen >= 7856 /* SLH-DSA-SHA2-128s/SLH-DSA-SHAKE-128s signature size */);
    return CryptoNative_EvpPKeyVerifyPreEncoded(pkey, extraHandle, msg, msgLen, sig, sigLen);
}

int32_t CryptoNative_SlhDsaExportSecretKey(const EVP_PKEY* pKey, uint8_t* destination, int32_t destinationLength)
{
    return EvpPKeyGetKeyOctetStringParam(pKey, OSSL_PKEY_PARAM_PRIV_KEY, destination, destinationLength);
}

int32_t CryptoNative_SlhDsaExportPublicKey(const EVP_PKEY* pKey, uint8_t* destination, int32_t destinationLength)
{
    return EvpPKeyGetKeyOctetStringParam(pKey, OSSL_PKEY_PARAM_PUB_KEY, destination, destinationLength);
}

int32_t CryptoNative_SlhDsaGetPalId(const EVP_PKEY* pKey, int32_t* slhDsaTypeId)
{
#ifdef NEED_OPENSSL_3_0
    assert(pKey && slhDsaTypeId);

    if (API_EXISTS(EVP_PKEY_is_a))
    {
        ERR_clear_error();

        // This conditional chain seems unavoidable. If there are multiple synonyms for a given key,
        // then the provider determines which one will be returned from EVP_PKEY_get0_type_name.
        // We use EVP_PKEY_is_a here instead to avoid this issue.
        if (EVP_PKEY_is_a(pKey, "SLH-DSA-SHA2-128s"))
        {
            *slhDsaTypeId = PalSlhDsaId_Sha2_128s;
        }
        else if (EVP_PKEY_is_a(pKey, "SLH-DSA-SHAKE-128s"))
        {
            *slhDsaTypeId = PalSlhDsaId_Shake128s;
        }
        else if (EVP_PKEY_is_a(pKey, "SLH-DSA-SHA2-128f"))
        {
            *slhDsaTypeId = PalSlhDsaId_Sha2_128f;
        }
        else if (EVP_PKEY_is_a(pKey, "SLH-DSA-SHAKE-128f"))
        {
            *slhDsaTypeId = PalSlhDsaId_Shake128f;
        }
        else if (EVP_PKEY_is_a(pKey, "SLH-DSA-SHA2-192s"))
        {
            *slhDsaTypeId = PalSlhDsaId_Sha2_192s;
        }
        else if (EVP_PKEY_is_a(pKey, "SLH-DSA-SHAKE-192s"))
        {
            *slhDsaTypeId = PalSlhDsaId_Shake192s;
        }
        else if (EVP_PKEY_is_a(pKey, "SLH-DSA-SHA2-192f"))
        {
            *slhDsaTypeId = PalSlhDsaId_Sha2_192f;
        }
        else if (EVP_PKEY_is_a(pKey, "SLH-DSA-SHAKE-192f"))
        {
            *slhDsaTypeId = PalSlhDsaId_Shake192f;
        }
        else if (EVP_PKEY_is_a(pKey, "SLH-DSA-SHA2-256s"))
        {
            *slhDsaTypeId = PalSlhDsaId_Sha2_256s;
        }
        else if (EVP_PKEY_is_a(pKey, "SLH-DSA-SHAKE-256s"))
        {
            *slhDsaTypeId = PalSlhDsaId_Shake256s;
        }
        else if (EVP_PKEY_is_a(pKey, "SLH-DSA-SHA2-256f"))
        {
            *slhDsaTypeId = PalSlhDsaId_Sha2_256f;
        }
        else if (EVP_PKEY_is_a(pKey, "SLH-DSA-SHAKE-256f"))
        {
            *slhDsaTypeId = PalSlhDsaId_Shake256f;
        }
        else
        {
            *slhDsaTypeId = PalSlhDsaId_Unknown;
        }

        return 1;
    }
#endif

    (void)pKey;
    *slhDsaTypeId = PalSlhDsaId_Unknown;
    return 0;
}

int32_t IsSlhDsaFamily(const EVP_PKEY* pKey)
{
    int slhDsaId = 0;
    return CryptoNative_SlhDsaGetPalId(pKey, &slhDsaId) &&
           slhDsaId != PalSlhDsaId_Unknown;
}
