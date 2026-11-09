// SPDX-License-Identifier: MIT
#pragma once
#include <Arduino.h>
#include <WebServer.h>

namespace studioWifi {
void begin(WebServer &server);
void poll();
// Adds comma-prefixed fields, before the caller closes its JSON object.
void appendStatus(String &json);
}
