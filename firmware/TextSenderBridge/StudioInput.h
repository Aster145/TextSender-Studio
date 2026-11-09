// SPDX-License-Identifier: MIT
// Copyright (c) 2026 TextSender Studio contributors
#pragma once
#include <stdint.h>
struct __attribute__((packed)) PadPacket {
  uint16_t buttons=0;
  uint8_t dpad=15,lx=128,ly=128,rx=128,ry=128,reserved=0;
};
static_assert(sizeof(PadPacket)==8,"USB pad packet length");
struct PadFrame { PadPacket packet; uint16_t duration=16; uint32_t queuedAt=0; };
struct KeyStroke { uint8_t modifier=0,usage=0; };
// US keyboard translation from USB HID Usage Tables; no locale fallback.
inline bool asciiStroke(uint8_t c,KeyStroke &out){
  out={};
  if(c>='a'&&c<='z'){out.usage=c-'a'+4;return true;}
  if(c>='A'&&c<='Z'){out.usage=c-'A'+4;out.modifier=2;return true;}
  if(c>='1'&&c<='9'){out.usage=c-'1'+30;return true;}
  if(c=='0'){out.usage=39;return true;}
  if(c=='\n'){out.usage=40;return true;}
  if(c=='\t'){out.usage=43;return true;}
  if(c==' '){out.usage=44;return true;}
  struct Pair {char normal,shifted;uint8_t usage;};
  constexpr Pair punctuation[]={
    {'1','!',30},{'2','@',31},{'3','#',32},{'4','$',33},{'5','%',34},
    {'6','^',35},{'7','&',36},{'8','*',37},{'9','(',38},{'0',')',39},
    {'-','_',45},{'=','+',46},{'[','{',47},{']','}',48},{'\\','|',49},
    {';',':',51},{'\'', '"',52},{'`','~',53},{',','<',54},{'.','>',55},{'/','?',56}
  };
  for(const auto &p:punctuation){if(c==p.normal||c==p.shifted){out.usage=p.usage;out.modifier=c==p.shifted?2:0;return true;}}
  return false;
}
