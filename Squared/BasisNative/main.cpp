#define BASISU_DEVEL_MESSAGES 1

#include <Windows.h>
#include "basisu_transcoder.h"
#include "../zstd/zstd.h"
#include <mutex>

using namespace basist;

std::mutex initializer_mutex;

bool is_initialized = false;

struct transcoder_info {
    bool isKtx2;
    basisu_transcoder * pBasis;
    ktx2_transcoder * pKtx2;
    void * pData;
};

BOOL WINAPI DllMain (
    _In_ HINSTANCE hinstDLL,
    _In_ DWORD     fdwReason,
    _In_ LPVOID    lpvReserved
) {
    return TRUE;
}

extern "C" {
    __declspec(dllexport) int32_t ZstdDecompress(unsigned char * result, int32_t result_size, unsigned const char * source, int32_t source_size) {
        size_t actualUncompSize = ZSTD_decompress(result, (size_t)result_size, source, (size_t)source_size);
        if (ZSTD_isError(actualUncompSize))
            return -1;
        return (int32_t)actualUncompSize;
    }

    transcoder_info __declspec(dllexport) * New (bool ktx2) {
        {
            std::lock_guard<std::mutex> guard(initializer_mutex);
            if (!is_initialized) {
                basisu_transcoder_init();
                is_initialized = true;
            }
        }

        transcoder_info * pResult = new transcoder_info();
        pResult->pData = 0;
        pResult->isKtx2 = ktx2;
        if (ktx2) {
            pResult->pKtx2 = new ktx2_transcoder();
            pResult->pBasis = 0;
        } else {
            pResult->pKtx2 = 0;
            pResult->pBasis = new basisu_transcoder();
        }

        return pResult;
    }

    void autoInit (transcoder_info * pTranscoder, void * pData, uint32_t dataSize) {
        if (pTranscoder->pData)
            return;
        pTranscoder->pData = pData;
        pTranscoder->pKtx2->init(pData, dataSize);
    }

    int __declspec(dllexport) Start (transcoder_info * pTranscoder, void * pData, uint32_t dataSize) {
        if (!pTranscoder)
            return 0;
        if (!pData)
            return 0;
        if (pTranscoder->pData && (pTranscoder->pData != pData))
            return 0;

        if (pTranscoder->isKtx2) {
            autoInit(pTranscoder, pData, dataSize);
            return pTranscoder->pKtx2->start_transcoding();
        } else
            return pTranscoder->pBasis->start_transcoding(pData, dataSize);
    }

    uint32_t __declspec(dllexport) GetTotalImages (transcoder_info * pTranscoder, void * pData, uint32_t dataSize) {
        if (!pTranscoder)
            return 0;
        if (!pData)
            return 0;

        if (pTranscoder->isKtx2)
            // FIXME
            return 1;
        else
            return pTranscoder->pBasis->get_total_images(pData, dataSize);
    }

    int __declspec(dllexport) GetImageInfo (
        transcoder_info * pTranscoder, void * pData,
        uint32_t dataSize, uint32_t imageIndex,
        basisu_image_info * pResult
    ) {
        if (!pTranscoder)
            return 0;
        if (!pData)
            return 0;
        if (!pResult)
            return 0;
        if (pTranscoder->pData && (pTranscoder->pData != pData))
            return 0;

        if (pTranscoder->isKtx2) {
            autoInit(pTranscoder, pData, dataSize);
            memset(pResult, 0, sizeof(*pResult));
            auto & header = pTranscoder->pKtx2->get_header();
            ktx2_image_level_info temp;
            memset(&temp, 0, sizeof(temp));
            if (!pTranscoder->pKtx2->get_image_level_info(temp, 0, 0, 0))
                return 0;
            pResult->m_width = header.m_pixel_width;
            pResult->m_height = header.m_pixel_height;
            pResult->m_total_levels = header.m_level_count;
            pResult->m_orig_width = temp.m_orig_width;
            pResult->m_orig_height = temp.m_orig_height;
            pResult->m_total_blocks = temp.m_total_blocks;
            pResult->m_num_blocks_x = temp.m_num_blocks_x;
            pResult->m_num_blocks_y = temp.m_num_blocks_y;
            pResult->m_alpha_flag = temp.m_alpha_flag;
            // FIXME: Rest of the fields
            return 1;
        } else {
            return pTranscoder->pBasis->get_image_info(pData, dataSize, *pResult, imageIndex);
        }
    }

    int __declspec(dllexport) GetImageLevelInfo (
        transcoder_info * pTranscoder, void * pData,
        uint32_t dataSize, uint32_t imageIndex, uint32_t levelIndex,
        basisu_image_level_info * pResult
    ) {
        if (!pTranscoder)
            return 0;
        if (!pData)
            return 0;
        if (!pResult)
            return 0;
        if (pTranscoder->pData && (pTranscoder->pData != pData))
            return 0;

        if (pTranscoder->isKtx2) {
            autoInit(pTranscoder, pData, dataSize);
            ktx2_image_level_info temp;
            memset(&temp, 0, sizeof(temp));
            if (!pTranscoder->pKtx2->get_image_level_info(temp, levelIndex, 0, 0))
                return 0;
            memset(pResult, 0, sizeof(*pResult));
            pResult->m_width = temp.m_width;
            pResult->m_height = temp.m_height;
            pResult->m_orig_width = temp.m_orig_width;
            pResult->m_orig_height = temp.m_orig_height;
            pResult->m_level_index = temp.m_level_index;
            pResult->m_total_blocks = temp.m_total_blocks;
            pResult->m_num_blocks_x = temp.m_num_blocks_x;
            pResult->m_num_blocks_y = temp.m_num_blocks_y;
            pResult->m_alpha_flag = temp.m_alpha_flag;
            // FIXME: Rest of the fields
            return 1;
        } else
            return pTranscoder->pBasis->get_image_level_info(pData, dataSize, *pResult, imageIndex, levelIndex);
    }

    int __declspec(dllexport) GetImageLevelDesc (
        transcoder_info * pTranscoder, void * pData,
        uint32_t dataSize, uint32_t imageIndex, uint32_t levelIndex,
        uint32_t *pOrigWidth, uint32_t *pOrigHeight, uint32_t *pTotalBlocks
    ) {
        if (!pTranscoder)
            return 0;
        if (!pData)
            return 0;
        if (!pOrigWidth || !pOrigHeight || !pTotalBlocks)
            return 0;
        if (pTranscoder->pData && (pTranscoder->pData != pData))
            return 0;

        if (pTranscoder->isKtx2) {
            autoInit(pTranscoder, pData, dataSize);
            ktx2_image_level_info temp;
            memset(&temp, 0, sizeof(temp));
            if (!pTranscoder->pKtx2->get_image_level_info(temp, levelIndex, 0, 0))
                return 0;
            if (pOrigWidth)
                *pOrigWidth = temp.m_orig_width;
            if (pOrigHeight)
                *pOrigHeight = temp.m_orig_height;
            if (pTotalBlocks)
                *pTotalBlocks = temp.m_total_blocks;
            return 1;
        } else
            return pTranscoder->pBasis->get_image_level_desc(pData, dataSize, imageIndex, levelIndex, *pOrigWidth, *pOrigHeight, *pTotalBlocks);
    }

    uint32_t __declspec(dllexport) GetBytesPerBlockOrPixel (transcoder_texture_format format) {
        return basis_get_bytes_per_block_or_pixel(format);
    }

    int __declspec(dllexport) TranscodeImageLevel (
        transcoder_info * pTranscoder, void * pData,
        uint32_t dataSize, uint32_t imageIndex, uint32_t levelIndex,
        void * pOutputBlocks, uint32_t outputBlocksSizeInBlocks,
        transcoder_texture_format format, uint32_t decodeFlags,
        uint32_t outputRowPitch, uint32_t outputHeightInPixels
    ) {
        if (!pTranscoder)
            return 0;
        if (!pData)
            return 0;
        if (!pOutputBlocks)
            return 0;
        if (pTranscoder->pData && (pTranscoder->pData != pData))
            return 0;

        if (pTranscoder->isKtx2) {
            autoInit(pTranscoder, pData, dataSize);
            return pTranscoder->pKtx2->transcode_image_level(
                levelIndex, 0, 0, pOutputBlocks,
                outputBlocksSizeInBlocks, format,
                decodeFlags, outputRowPitch, outputHeightInPixels
            );
        } else
            return pTranscoder->pBasis->transcode_image_level(
                pData, dataSize, imageIndex, levelIndex, 
                pOutputBlocks, outputBlocksSizeInBlocks,
                format, decodeFlags, outputRowPitch, nullptr, outputHeightInPixels
            );
    }

    void __declspec(dllexport) Delete (transcoder_info * pTranscoder) {
        if (!pTranscoder)
            return;

        if (pTranscoder->isKtx2)
            delete pTranscoder->pKtx2;
        else
            delete pTranscoder->pBasis;
        delete pTranscoder;
    }
}