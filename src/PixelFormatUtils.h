#pragma once

#include "UploadQueue.h"

#include <vector>

void ConvertBGRAtoRGBA(std::vector<uint8_t>& buffer, const UploadContext& ctx);
