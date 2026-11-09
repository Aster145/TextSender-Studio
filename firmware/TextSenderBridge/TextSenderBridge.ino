// SPDX-License-Identifier: MIT
// Copyright (c) 2026 TextSender Studio contributors
// Independent implementation of the documented TextSender HTTP input protocol.
#include <Arduino.h>
#include <WebServer.h>
#include <USB.h>
#include "esp_system.h"
#include "StudioInput.h"
#include "StudioUsb.h"
#include "StudioWifi.h"

WebServer http(80);
SemaphoreHandle_t runtimeMutex;
uint32_t bootId,padEpoch;
bool usbStarted=false;
String keyboardJob,keyboardError,keyboardText;
size_t keySent=0,keyTotal=0;
uint32_t keyDue=0,keyGap=30,keyLine=150;
bool keyBusy=false,keyTap=false,keyDown=false,keyNeutral=true;
KeyStroke tapStroke;
String keySession;
uint32_t keySequence=0,keyLeaseAt=0;
bool keyLeased=false;
String padJob,padError,padSession;
PadPacket padCurrent;
PadFrame frames[16];
uint8_t frameHead=0,frameCount=0;
bool frameDelivered=false,padLeased=false,padBusy=false,padNeutral=true,padCompleted=false;
uint32_t padSequence=0,padLeaseAt=0,padApplied=0,padHold=0,padReports=0;

class RuntimeLock {
  bool held;
public:
  RuntimeLock():held(xSemaphoreTake(runtimeMutex,pdMS_TO_TICKS(250))==pdTRUE){}
  ~RuntimeLock(){if(held)xSemaphoreGive(runtimeMutex);}
  explicit operator bool()const{return held;}
};
void errorReply(int code,const char *error){http.send(code,"application/json",String("{\"error\":\"")+error+"\"}");}
void okReply(){http.send(200,"application/json","{\"accepted\":true}");}
bool readNumber(const String &name,uint32_t maximum,uint32_t &value){
  if(!http.hasArg(name))return false;
  String s=http.arg(name);if(s.isEmpty()||s.length()>10)return false;
  uint64_t n=0;for(unsigned i=0;i<s.length();i++){if(s[i]<'0'||s[i]>'9')return false;n=n*10+s[i]-'0';if(n>maximum)return false;}
  value=uint32_t(n);return true;
}
bool validSession(const String &id){if(id.length()!=32)return false;for(unsigned i=0;i<32;i++)if(!isHexadecimalDigit(id[i]))return false;return true;}
bool validJob(const String &id){if(id.isEmpty()||id.length()>64)return false;for(unsigned i=0;i<id.length();i++)if(!isAlphaNumeric(id[i])&&id[i]!='-')return false;return true;}
void clearKeyboard(const char *why){keyBusy=false;keyLeased=false;keyDown=false;keyNeutral=true;keyboardText="";keyboardError=why;}
void clearPad(const char *why){
  padLeased=false;padBusy=false;padNeutral=true;padCompleted=false;frameCount=frameHead=0;frameDelivered=false;padCurrent={};padError=why;++padEpoch;
}
void clearInputs(const char *why){clearKeyboard(why);clearPad(why);}
bool readyRequest(){if(!usbStarted||!studioUsb::readyForInput()){errorReply(409,"usb_not_ready");return false;}return true;}

void getStatus(){
  RuntimeLock lock;if(!lock){errorReply(503,"runtime_busy");return;}
  String s; s.reserve(1100);
  s="{\"firmware\":\"textsender-studio-0.1.1\",\"mode\":\"dual\",\"usb_interfaces\":2,\"boot_id\":"+String(bootId);
  s+=",\"usb_mounted\":";s+=studioUsb::available()?"true":"false";
  s+=",\"input_ready\":";s+=studioUsb::readyForInput()?"true":"false";
  s+=",\"busy\":";s+=keyBusy?"true":"false";
  s+=",\"release_pending\":";s+=keyNeutral?"true":"false";
  s+=",\"keyboard_report_active\":";s+=keyLeased?"true":"false";
  s+=",\"sent\":"+String(keySent)+",\"total\":"+String(keyTotal)+",\"leds\":"+String(studioUsb::leds());
  s+=",\"job\":\""+keyboardJob+"\",\"error\":\""+keyboardError+"\"";
  s+=",\"gamepad\":{\"epoch\":"+String(padEpoch)+",\"busy\":";s+=(padBusy||padLeased)?"true":"false";
  s+=",\"release_pending\":";s+=padNeutral?"true":"false";
  s+=",\"input_ready\":";s+=studioUsb::readyForInput()?"true":"false";
  s+=",\"completed\":";s+=padCompleted?"true":"false";
  s+=",\"job\":\""+padJob+"\",\"error\":\""+padError+"\",\"reports_ok\":"+String(padReports);
  s+=",\"stream\":{\"supported\":true,\"active\":";s+=padLeased?"true":"false";
  s+=",\"queued\":"+String(frameCount)+",\"next_sequence\":"+String(padSequence)+",\"capacity\":16,\"lease_ms\":600}}";
  s+=",\"connection_control\":{\"usb_enabled\":";s+=studioUsb::enabled()?"true":"false";s+="}";
  studioWifi::appendStatus(s);s+="}";
  http.send(200,"application/json",s);
}

void startKeyboard(bool tap){
  if(!readyRequest())return;
  uint32_t gap,line,usage=0,modifier=0;
  String id=http.header("X-Job"),text=http.arg("plain");
  if(!validJob(id)||!readNumber("gap",60000,gap)||!readNumber("line",60000,line)){errorReply(400,"invalid_job_or_timing");return;}
  if(tap){if(!readNumber("key",0xe7,usage)||usage<4||!readNumber("mod",255,modifier)){errorReply(400,"invalid_key");return;}}
  else{
    if(text.isEmpty()||text.length()>16384){errorReply(400,"text_size_1_to_16384");return;}
    if(http.hasArg("layout")&&http.arg("layout")!="us"){errorReply(400,"layout_us_required");return;}
    for(unsigned i=0;i<text.length();i++){KeyStroke stroke;if(!asciiStroke(text[i],stroke)){errorReply(400,"ascii_only");return;}}
  }
  RuntimeLock lock;if(!lock){errorReply(503,"runtime_busy");return;}
  if(keyBusy||keyLeased||keyNeutral){errorReply(409,"busy_or_releasing");return;}
  if(id==keyboardJob){errorReply(409,"duplicate_job_id");return;}
  keyboardJob=id;keyboardError="";keyboardText=text;keyTotal=tap?1:text.length();keySent=0;
  tapStroke={uint8_t(modifier),uint8_t(usage)};keyTap=tap;keyBusy=true;keyDown=false;keyGap=gap;keyLine=line;keyDue=millis();
  http.send(202,"application/json","{\"accepted\":true,\"job\":\""+id+"\"}");
}

void keyboardReport(){
  if(!readyRequest())return;
  String id=http.arg("session");uint32_t sequence,modifier;
  if(!validSession(id)||!readNumber("sequence",UINT32_MAX,sequence)||!readNumber("mod",255,modifier)){errorReply(400,"invalid_keyboard_report");return;}
  uint8_t bytes[8]={uint8_t(modifier),0};bool held=modifier!=0;
  for(unsigned i=0;i<6;i++){uint32_t usage;if(!readNumber("k"+String(i),0xe7,usage)||(usage&&usage<4)){errorReply(400,"invalid_key_usage");return;}bytes[i+2]=usage;held|=usage!=0;}
  RuntimeLock lock;if(!lock){errorReply(503,"runtime_busy");return;}
  if(keyBusy||keyNeutral){errorReply(409,"busy_or_releasing");return;}
  if(keyLeased){
    if(id!=keySession){errorReply(409,"keyboard_owned");return;}
    if(sequence==keySequence-1){okReply();return;}
    if(sequence!=keySequence){errorReply(409,"keyboard_sequence");return;}
    if(uint32_t(millis()-keyLeaseAt)>=600){clearKeyboard("keyboard_lease_expired");errorReply(409,"keyboard_expired");return;}
  }else if(sequence||id==keySession){errorReply(409,"keyboard_expired");return;}
  if(!studioUsb::write(studioUsb::Keys,bytes)){clearKeyboard("keyboard_report_failed");errorReply(503,"keyboard_report_failed");return;}
  keySession=id;keySequence=sequence+1;keyLeased=held;keyLeaseAt=millis();keyboardError="";okReply();
}

bool parseFrames(const String &encoded,PadFrame parsed[8],unsigned &count){
  count=0;const char *p=encoded.c_str();
  while(*p){
    if(count==8)return false;
    uint32_t v[7];const unsigned limit[7]={16383,15,255,255,255,255,200};
    for(unsigned field=0;field<7;field++){
      if(*p<'0'||*p>'9')return false;uint64_t n=0;
      while(*p>='0'&&*p<='9'){n=n*10+*p++-'0';if(n>limit[field])return false;}v[field]=n;
      if(field<6){if(*p!=',')return false;p++;}
    }
    if((v[1]>7&&v[1]!=15)||v[6]<8)return false;
    parsed[count++]={PadPacket{uint16_t(v[0]),uint8_t(v[1]),uint8_t(v[2]),uint8_t(v[3]),uint8_t(v[4]),uint8_t(v[5]),0},uint16_t(v[6]),millis()};
    if(*p){if(*p!=';')return false;p++;}
  }return true;
}
void streamPad(){
  if(!readyRequest())return;
  String id=http.arg("session"),encoded=http.arg("frames");uint32_t sequence,epoch;PadFrame incoming[8];unsigned count;
  if(!validSession(id)||encoded.length()>1024||!readNumber("sequence",UINT32_MAX,sequence)||!readNumber("epoch",UINT32_MAX,epoch)||!parseFrames(encoded,incoming,count)){errorReply(400,"invalid_pad_stream");return;}
  RuntimeLock lock;if(!lock){errorReply(503,"runtime_busy");return;}
  if(epoch!=padEpoch){errorReply(409,"stale_epoch");return;}
  if(padBusy||padNeutral){errorReply(409,"busy_or_releasing");return;}
  if(padLeased){
    if(id!=padSession){errorReply(409,"stream_owned");return;}
    if(sequence==padSequence-1){http.send(200,"application/json","{\"accepted\":true,\"next_sequence\":"+String(padSequence)+"}");return;}
    if(sequence!=padSequence){errorReply(409,"stream_sequence");return;}
    if(uint32_t(millis()-padLeaseAt)>=600){clearPad("stream_expired");errorReply(409,"stream_expired");return;}
  }else if(sequence||!count||id==padSession){errorReply(409,"invalid_stream_start");return;}
  if(count>16-frameCount){errorReply(409,"stream_queue_full");return;}
  for(unsigned i=0;i<count;i++)frames[(frameHead+frameCount++)%16]=incoming[i];
  padLeased=true;padSession=id;padSequence=sequence+1;padLeaseAt=millis();padError="";
  http.send(200,"application/json","{\"accepted\":true,\"next_sequence\":"+String(padSequence)+",\"queued\":"+String(frameCount)+",\"capacity\":16}");
}
void pulsePad(){
  if(!readyRequest())return;
  uint32_t button,dpad,lx,ly,rx,ry,hold,epoch;String id=http.header("X-Job");
  if(!validJob(id)||!readNumber("buttons",16383,button)||!readNumber("dpad",15,dpad)||(dpad>7&&dpad!=15)||!readNumber("lx",255,lx)||!readNumber("ly",255,ly)||!readNumber("rx",255,rx)||!readNumber("ry",255,ry)||!readNumber("hold",60000,hold)||!hold||!readNumber("epoch",UINT32_MAX,epoch)){errorReply(400,"invalid_pad_input");return;}
  RuntimeLock lock;if(!lock){errorReply(503,"runtime_busy");return;}
  if(epoch!=padEpoch||padLeased||padBusy||padNeutral||id==padJob){errorReply(409,"pad_unavailable");return;}
  padCurrent={uint16_t(button),uint8_t(dpad),uint8_t(lx),uint8_t(ly),uint8_t(rx),uint8_t(ry),0};
  padHold=hold;padJob=id;padBusy=true;padCompleted=false;padError="";frameDelivered=false;padApplied=millis();
  http.send(202,"application/json","{\"accepted\":true,\"job\":\""+id+"\"}");
}

void usbWorker(void*){
  for(;;){
    {RuntimeLock lock;if(lock){
      studioUsb::poll();uint32_t now=millis();
      if(!studioUsb::available()){
        if(keyBusy||keyLeased)clearKeyboard("usb_disconnected");
        if(padBusy||padLeased)clearPad("usb_disconnected");
      }else{
        if(keyLeased&&uint32_t(now-keyLeaseAt)>=600)clearKeyboard("keyboard_lease_expired");
        if(padLeased&&(uint32_t(now-padLeaseAt)>=600||(frameCount&&uint32_t(now-frames[frameHead].queuedAt)>=500)))clearPad("stream_expired");
        if(keyNeutral){uint8_t neutral[8]={};if(studioUsb::write(studioUsb::Keys,neutral))keyNeutral=false;}
        else if(keyBusy&&int32_t(now-keyDue)>=0&&keySent==keyTotal){keyBusy=false;keyboardText="";}
        else if(keyBusy&&int32_t(now-keyDue)>=0){
          uint8_t bytes[8]={};
          if(!keyDown){KeyStroke stroke=tapStroke;if(!keyTap)asciiStroke(keyboardText[keySent],stroke);bytes[0]=stroke.modifier;bytes[2]=stroke.usage;}
          if(!studioUsb::write(studioUsb::Keys,bytes))clearKeyboard("usb_transfer_failed");
          else{
            keyDue=millis()+keyGap;
            if(keyDown){if(!keyTap&&keyboardText[keySent]=='\n')keyDue+=keyLine;++keySent;}
            keyDown=!keyDown;
          }
        }
        if(padBusy&&frameDelivered&&uint32_t(now-padApplied)>=padHold){padCurrent={};padNeutral=true;padBusy=false;}
        if(padLeased&&frameCount){
          if(frameDelivered&&uint32_t(now-padApplied)>=frames[frameHead].duration){frameHead=(frameHead+1)%16;--frameCount;frameDelivered=false;}
          if(frameCount&&!frameDelivered){padCurrent=frames[frameHead].packet;}
        }
        const bool sent=studioUsb::write(studioUsb::Pad,reinterpret_cast<uint8_t*>(&padCurrent));
        if(sent){
          ++padReports;
          if(padNeutral){padNeutral=false;if(!padJob.isEmpty())padCompleted=true;}
          if((padBusy||(padLeased&&frameCount))&&!frameDelivered){frameDelivered=true;padApplied=millis();}
        }else if(padBusy||padLeased)clearPad("usb_transfer_failed");
      }
    }}
    vTaskDelay(pdMS_TO_TICKS(4));
  }
}

void setup(){
  runtimeMutex=xSemaphoreCreateMutex();bootId=esp_random();padEpoch=esp_random();
  usbStarted=studioUsb::start();USB.VID(0x0f0d);USB.PID(0x00c1);
  USB.productName("TextSender Studio Keyboard Gamepad");USB.manufacturerName("TextSender Studio");USB.serialNumber("TEXTSENDER-STUDIO");
  USB.usbClass(0);USB.usbSubClass(0);USB.usbProtocol(0);USB.usbPower(100);
  usbStarted=USB.begin()&&usbStarted;
  if(!runtimeMutex||xTaskCreatePinnedToCore(usbWorker,"studio-usb",6144,nullptr,2,nullptr,1)!=pdPASS)usbStarted=false;
  studioWifi::begin(http);
  const char *headers[]={"X-Job"};http.collectHeaders(headers,1);
  http.on("/status",HTTP_GET,getStatus);
  http.on("/text",HTTP_POST,[]{startKeyboard(false);});http.on("/tap",HTTP_POST,[]{startKeyboard(true);});
  http.on("/keyboard/report",HTTP_POST,keyboardReport);http.on("/pad",HTTP_POST,pulsePad);http.on("/pad/stream",HTTP_POST,streamPad);
  http.on("/cancel",HTTP_POST,[]{RuntimeLock lock;if(!lock){errorReply(503,"runtime_busy");return;}clearInputs("cancelled");http.send(200,"application/json","{\"cancelled\":true}");});
  http.on("/disconnect",HTTP_POST,[]{RuntimeLock lock;if(!lock){errorReply(503,"runtime_busy");return;}clearInputs("disconnected");uint8_t keys[8]={};studioUsb::write(studioUsb::Keys,keys);studioUsb::write(studioUsb::Pad,reinterpret_cast<uint8_t*>(&padCurrent));if(!studioUsb::enable(false)){errorReply(503,"disconnect_failed");return;}okReply();});
  http.on("/connect",HTTP_POST,[]{RuntimeLock lock;if(!lock){errorReply(503,"runtime_busy");return;}clearInputs("reconnected");if(!studioUsb::enable(true)){errorReply(503,"connect_failed");return;}okReply();});
  http.begin();
}
void loop(){studioWifi::poll();http.handleClient();delay(1);}
