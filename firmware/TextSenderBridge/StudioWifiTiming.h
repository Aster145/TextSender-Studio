// SPDX-License-Identifier: MIT
#pragma once
#include <stdint.h>

namespace studioWifi {
inline bool due(uint32_t now,uint32_t deadline){return int32_t(now-deadline)>=0;}
inline uint32_t remaining(uint32_t now,uint32_t deadline){return due(now,deadline)?0:deadline-now;}

enum class Phase {None,DeferredDisconnect,Prepare,Connecting,Waiting,Connected};
struct ConnectionSchedule {
  static constexpr uint32_t ConnectWindow=30000,RetryDelay=10000,PortalGrace=120000;
  Phase phase=Phase::None;
  uint32_t outageAt=0,nextActionAt=0,attemptAt=0,apCloseAt=0;
  bool apClosePending=false;
  void start(bool configured,uint32_t now){
    phase=configured?Phase::Prepare:Phase::None;outageAt=now;nextActionAt=now+100;apClosePending=false;
  }
  void saved(uint32_t now){phase=Phase::DeferredDisconnect;nextActionAt=now+500;outageAt=now;apClosePending=false;}
  void joined(uint32_t now,bool portalActive){phase=Phase::Connected;apClosePending=portalActive;if(portalActive)apCloseAt=now+PortalGrace;}
  void lost(uint32_t now){phase=Phase::Waiting;outageAt=now;nextActionAt=now+3000;apClosePending=false;}
  void retry(uint32_t now){phase=Phase::Waiting;nextActionAt=now+RetryDelay;}
  void disconnected(uint32_t now){phase=Phase::Prepare;nextActionAt=now+300;}
  void connecting(uint32_t now){phase=Phase::Connecting;attemptAt=now;}
  bool needsPortal(bool configured,uint32_t now)const{return !configured||(phase!=Phase::Connected&&uint32_t(now-outageAt)>=ConnectWindow);}
  bool attemptExpired(uint32_t now)const{return phase==Phase::Connecting&&uint32_t(now-attemptAt)>=ConnectWindow;}
  bool canScan()const{return phase!=Phase::Connecting&&phase!=Phase::Prepare&&phase!=Phase::DeferredDisconnect;}
  bool canStart(uint32_t now,bool scanRunning,bool scanRequested)const{
    return !scanRunning&&(phase==Phase::Prepare||phase==Phase::Waiting)&&due(now,nextActionAt)&&(phase==Phase::Prepare||!scanRequested);
  }
  bool shouldClosePortal(bool active,uint32_t now)const{return active&&phase==Phase::Connected&&apClosePending&&due(now,apCloseAt);}
};

// A button held during power-on must first be released. One hold fires once.
class ResetHold {
  bool armed=false,pressed=false;
  uint32_t pressedAt=0;
public:
  bool update(bool down,uint32_t now){
    if(!down){armed=true;pressed=false;return false;}
    if(!armed)return false;
    if(!pressed){pressed=true;pressedAt=now;return false;}
    if(uint32_t(now-pressedAt)<5000)return false;
    armed=false;pressed=false;return true;
  }
};
}
