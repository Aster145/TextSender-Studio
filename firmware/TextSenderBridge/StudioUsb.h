// SPDX-License-Identifier: MIT
// Copyright (c) 2026 TextSender Studio contributors
#pragma once
#include <Arduino.h>
namespace studioUsb {
enum Device : uint8_t { Pad, Keys };
bool start();
bool available();
bool readyForInput();
bool write(Device device,const uint8_t packet[8],uint32_t timeout=50);
bool enable(bool value);
bool enabled();
uint8_t leds();
void poll();
}
