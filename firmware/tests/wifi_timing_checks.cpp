// SPDX-License-Identifier: MIT
#include "../TextSenderBridge/StudioWifiTiming.h"
#include <stdio.h>
#include <stdlib.h>
using namespace studioWifi;
static int checks=0;
static void check(bool passed,const char *message){
  if(!passed){fprintf(stderr,"FAIL: %s\n",message);exit(1);}++checks;
}
int main(){
  ConnectionSchedule schedule;
  schedule.start(false,1000);
  check(schedule.needsPortal(false,1000),"unconfigured boot immediately needs portal");
  check(!schedule.canStart(1100,false,false),"unconfigured boot never connects stale SDK credentials");
  schedule.start(true,1000);
  check(!schedule.needsPortal(true,1000),"configured boot starts without hotspot");
  check(!schedule.canStart(1099,false,false),"initial preparation waits");
  check(schedule.canStart(1100,false,false),"saved credentials connect at startup");
  check(schedule.canStart(1100,false,true),"queued scan cannot deadlock initial preparation");
  check(!schedule.canStart(1100,true,true),"active scan blocks another connection");
  schedule.connecting(1100);
  check(!schedule.canScan(),"initial connection completes before scan starts");
  check(!schedule.needsPortal(true,30999),"fallback does not open prematurely");
  check(schedule.needsPortal(true,31000),"fallback starts after thirty seconds");
  check(!schedule.attemptExpired(31099),"connection attempt has its own deadline");
  check(schedule.attemptExpired(31100),"connection attempt eventually expires");
  schedule.retry(31100);
  check(schedule.canScan(),"retry backoff allows queued scan");
  check(!schedule.canStart(41099,false,false),"retry backoff is ten seconds");
  check(schedule.canStart(41100,false,false),"retry runs after backoff");
  check(!schedule.canStart(41100,false,true),"queued scan takes priority over retry");
  check(!schedule.canStart(41100,true,false),"running scan blocks retry");
  schedule.joined(50000,false);
  check(!schedule.needsPortal(true,500000),"connected normal boot stays station only");
  check(!schedule.shouldClosePortal(false,500000),"no AP shutdown when no portal exists");
  schedule.joined(50000,true);
  check(!schedule.shouldClosePortal(true,169999),"successful provisioning keeps IP page for two minutes");
  check(schedule.shouldClosePortal(true,170000),"successful portal closes after two minutes");
  schedule.lost(90000);
  check(!schedule.shouldClosePortal(true,170000),"connection loss cancels portal shutdown");
  check(!schedule.canStart(92999,false,false),"connection loss waits briefly before retry");
  check(schedule.canStart(93000,false,false),"connection loss retries");
  check(!schedule.needsPortal(true,119999),"short outage leaves hotspot off");
  check(schedule.needsPortal(true,120000),"continued outage restores setup hotspot");
  schedule.saved(200000);
  check(schedule.phase==Phase::DeferredDisconnect&&!schedule.canStart(200500,false,false),"save response precedes disconnect and connect");
  schedule.disconnected(200500);
  check(!schedule.canStart(200799,false,false)&&schedule.canStart(200800,false,true),"new configuration settles then connects even with queued scan");

  const uint32_t nearWrap=UINT32_MAX-1000;
  schedule.start(true,nearWrap);schedule.connecting(nearWrap+100);
  check(!schedule.needsPortal(true,uint32_t(nearWrap+29999)),"fallback timer handles millis wrap before deadline");
  check(schedule.needsPortal(true,uint32_t(nearWrap+30000)),"fallback timer handles millis wrap at deadline");
  schedule.joined(nearWrap,true);
  check(!schedule.shouldClosePortal(true,uint32_t(nearWrap+119999)),"portal grace survives millis wrap");
  check(schedule.shouldClosePortal(true,uint32_t(nearWrap+120000)),"portal expiry survives millis wrap");
  check(remaining(nearWrap,nearWrap+2000)==2000&&remaining(1000,999)==0,"countdowns handle wrapping and elapsed deadlines");

  ResetHold button;
  check(!button.update(true,0)&&!button.update(true,20000),"BOOT held at startup cannot erase Wi-Fi");
  check(!button.update(false,20001)&&!button.update(true,20002),"BOOT must be released then pressed");
  check(!button.update(true,25001)&&button.update(true,25002),"BOOT triggers only after full five second hold");
  check(!button.update(true,50000),"continued hold does not clear repeatedly");
  check(!button.update(false,50001)&&!button.update(true,50002)&&button.update(true,55002),"new deliberate hold can reset again");
  ResetHold wrappedButton;
  wrappedButton.update(false,nearWrap);wrappedButton.update(true,nearWrap+1);
  check(!wrappedButton.update(true,uint32_t(nearWrap+5000))&&wrappedButton.update(true,uint32_t(nearWrap+5001)),"BOOT hold duration handles millis wrap");
  printf("Wi-Fi timing checks passed: %d\n",checks);return 0;
}
