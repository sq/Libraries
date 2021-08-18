#define BASISU_DEVEL_MESSAGES 1

#include <Windows.h>
#include "basisu_transcoder.h"
#include <mutex>

using namespace basist;

etc1_global_selector_codebook sel_codebook;
std::mutex initializer_mutex;

bool is_initialized = false;

BOOL WINAPI DllMain (
    _In_ HINSTANCE hinstDLL,
    _In_ DWORD     fdwReason,
    _In_ LPVOID    lpvReserved
) {
    return TRUE;
}

extern "C" {
    basisu_transcoder __declspec(dllexport) * New () {
        {
            std::lock_guard<std::mutex> guard(initializer_mutex);
            if (!is_initialized) {
                basist::basisu_transcoder_init();

                sel_codebook = etc1_global_selector_codebook(
                    basist::g_global_selector_cb_size, basist::g_global_selector_cb
                );

                is_initialized = true;
            }
        }

        basisu_transcoder * pTranscoder = new basisu_transcoder(&sel_codebook);

        return pTranscoder;
    }

    int __declspec(dllexport) Start (basisu_transcoder * pTranscoder, void * pData, uint32_t dataSize) {
        if (!pTranscoder)
            return 0;
        if (!pData)
            return 0;

        return pTranscoder->start_transcoding(pData, dataSize);
    }

    uint32_t __declspec(dllexport) GetTotalImages (basisu_transcoder * pTranscoder, void * pData, uint32_t dataSize) {
        if (!pTranscoder)
            return 0;
        if (!pData)
            return 0;

        return pTranscoder->get_total_images(pData, dataSize);
    }

    int __declspec(dllexport) GetImageInfo (
        basisu_transcoder * pTranscoder, void * pData, 
        uint32_t dataSize, uint32_t imageIndex,
        basisu_image_info * pResult
    ) {
        if (!pTranscoder)
            return 0;
        if (!pData)
            return 0;
        if (!pResult)
            return 0;

        return pTranscoder->get_image_info(pData, dataSize, *pResult, imageIndex);
    }

    int __declspec(dllexport) GetImageLevelInfo (
        basisu_transcoder * pTranscoder, void * pData,
        uint32_t dataSize, uint32_t imageIndex, uint32_t levelIndex,
        basisu_image_level_info * pResult
    ) {
        if (!pTranscoder)
            return 0;
        if (!pData)
            return 0;
        if (!pResult)
            return 0;

        return pTranscoder->get_image_level_info(pData, dataSize, *pResult, imageIndex, levelIndex);
    }

    int __declspec(dllexport) GetImageLevelDesc (
        basisu_transcoder * pTranscoder, void * pData,
        uint32_t dataSize, uint32_t imageIndex, uint32_t levelIndex,
        uint32_t *pOrigWidth, uint32_t *pOrigHeight, uint32_t *pTotalBlocks
    ) {
        if (!pTranscoder)
            return 0;
        if (!pData)
            return 0;
        if (!pOrigWidth || !pOrigHeight || !pTotalBlocks)
            return 0;

        return pTranscoder->get_image_level_desc(pData, dataSize, imageIndex, levelIndex, *pOrigWidth, *pOrigHeight, *pTotalBlocks);
    }

    uint32_t __declspec(dllexport) GetBytesPerBlock (transcoder_texture_format format) {
        return basis_get_bytes_per_block_or_pixel(format);
    }

    int __declspec(dllexport) TranscodeImageLevel (
        basisu_transcoder * pTranscoder, void * pData,
        uint32_t dataSize, uint32_t imageIndex, uint32_t levelIndex,
        void * pOutputBlocks, uint32_t outputBlocksSizeInBlocks,
        transcoder_texture_format format, uint32_t decodeFlags
    ) {
        if (!pTranscoder)
            return 0;
        if (!pData)
            return 0;
        if (!pOutputBlocks)
            return 0;

        return pTranscoder->transcode_image_level(
            pData, dataSize, imageIndex, levelIndex, 
            pOutputBlocks, outputBlocksSizeInBlocks,
            format, decodeFlags
        );
    }

    void __declspec(dllexport) Delete (basisu_transcoder * pTranscoder) {
        if (!pTranscoder)
            return;

        delete pTranscoder;
    }
}