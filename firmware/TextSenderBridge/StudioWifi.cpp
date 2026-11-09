// SPDX-License-Identifier: MIT
// Wi-Fi provisioning and reconnect policy; all mutations run on the loop task.
#include "StudioWifi.h"
#include "StudioWifiTiming.h"
#include "WifiPortalPage.h"
#include <WiFi.h>
#include <DNSServer.h>
#include <Preferences.h>
#include <esp_wifi.h>
#include <atomic>
#include <algorithm>
#include <vector>
#include <stddef.h>
#include <string.h>

namespace studioWifi {
namespace {
constexpr uint8_t ResetPin=0;
constexpr char HotspotName[]="TextSender-Setup";
constexpr char StorageNamespace[]="tsstudio";
constexpr char StorageKey[]="wifi_v1";
constexpr uint32_t StorageMagic=0x31575354;
struct Credentials {
  uint32_t magic;
  uint8_t version,ssidLength,passwordLength,reserved;
  char ssid[33],password[64];
  uint8_t padding[3];
  uint32_t checksum;
};
WebServer *http=nullptr;
DNSServer dns;
Credentials credentials{};
ConnectionSchedule schedule;
bool configured=false,apActive=false,scanRequested=false,scanRunning=false,scanAvailable=false;
bool storageError=false;
uint32_t scanAt=0;
uint32_t lastReason=0;
std::atomic<uint32_t> disconnectReason{0};
String scanList="[]",scanMessage,localError;
ResetHold resetButton;

uint32_t checksum(const Credentials &value){
  uint32_t result=2166136261u;const auto *bytes=reinterpret_cast<const uint8_t*>(&value);
  for(size_t i=0;i<offsetof(Credentials,checksum);++i)result=(result^bytes[i])*16777619u;
  return result;
}
bool valid(const Credentials &value){
  if(value.magic!=StorageMagic||value.version!=1||value.ssidLength>32||value.passwordLength>63)return false;
  if(value.ssid[value.ssidLength]||value.password[value.passwordLength])return false;
  if(strlen(value.ssid)!=value.ssidLength||strlen(value.password)!=value.passwordLength)return false;
  if(!value.ssidLength&&value.passwordLength)return false;
  if(value.passwordLength&&value.passwordLength<8)return false;
  return value.checksum==checksum(value);
}
Credentials makeCredentials(const String &ssid,const String &password){
  Credentials value{};value.magic=StorageMagic;value.version=1;
  value.ssidLength=ssid.length();value.passwordLength=password.length();
  memcpy(value.ssid,ssid.c_str(),value.ssidLength);memcpy(value.password,password.c_str(),value.passwordLength);
  value.checksum=checksum(value);return value;
}
bool saveCredentials(const Credentials &value){
  Preferences store;if(!store.begin(StorageNamespace,false))return false;
  // One NVS blob commits both fields atomically, including the cleared tombstone.
  bool saved=store.putBytes(StorageKey,&value,sizeof(value))==sizeof(value);
  Credentials check{};
  saved=saved&&store.getBytes(StorageKey,&check,sizeof(check))==sizeof(check)&&memcmp(&value,&check,sizeof(value))==0;
  if(saved){if(store.isKey("ssid"))store.remove("ssid");if(store.isKey("password"))store.remove("password");}
  store.end();return saved;
}
void loadCredentials(){
  Preferences store;
  if(!store.begin(StorageNamespace,false)){storageError=true;localError="无法读取已保存的 Wi-Fi 配置，请重新配网。";return;}
  if(store.isKey(StorageKey)){
    bool loaded=store.getBytesLength(StorageKey)==sizeof(credentials)&&store.getBytes(StorageKey,&credentials,sizeof(credentials))==sizeof(credentials)&&valid(credentials);
    store.end();
    if(!loaded){credentials={};storageError=true;localError="保存的 Wi-Fi 配置无效，请重新配网。";return;}
    configured=credentials.ssidLength!=0;return;
  }
  String ssid=store.getString("ssid",""),password=store.getString("password","");store.end();
  if(ssid.isEmpty())return;
  if(ssid.length()>32||password.length()>63||(!password.isEmpty()&&password.length()<8)){
    storageError=true;localError="旧 Wi-Fi 配置无效，请重新配网。";return;
  }
  credentials=makeCredentials(ssid,password);configured=true;
  if(!saveCredentials(credentials)){storageError=true;localError="旧配置可以使用，但迁移保存失败，请重新保存配网。";}
}
String quote(const String &value){
  String out="\"";const char hex[]="0123456789abcdef";
  for(size_t i=0;i<value.length();++i){uint8_t c=value[i];
    if(c=='"'||c=='\\'){out+='\\';out+=char(c);}
    else if(c<32){out+="\\u00";out+=hex[c>>4];out+=hex[c&15];}
    else out+=char(c);
  }
  out+='"';return out;
}
void sendJson(int code,const String &body){http->sendHeader("Cache-Control","no-store");http->send(code,"application/json; charset=utf-8",body);}
bool stationConnected(){
  return configured&&schedule.phase!=Phase::DeferredDisconnect&&schedule.phase!=Phase::Prepare&&WiFi.status()==WL_CONNECTED&&uint32_t(WiFi.localIP())!=0;
}
void openPortal(){
  if(apActive)return;
  WiFi.mode(WIFI_AP_STA);
  if(!WiFi.softAP(HotspotName,nullptr,1,false,4)){localError="配网热点启动失败，稍后重试。";return;}
  WiFi.AP.enableDhcpCaptivePortal();
  apActive=true;schedule.apClosePending=false;
  dns.setTTL(0);dns.start(53,"*",WiFi.softAPIP());
}
void closePortal(){
  if(!apActive)return;
  dns.stop();WiFi.softAPdisconnect(true);apActive=false;schedule.apClosePending=false;
}
const char *reasonMessage(uint32_t reason){
  switch(reason){
    case WIFI_REASON_NO_AP_FOUND:return "没有找到此 Wi-Fi，请确认路由器已开启 2.4 GHz 网络。";
    case WIFI_REASON_AUTH_FAIL:return "路由器拒绝认证，请检查 Wi-Fi 密码和加密设置。";
    case WIFI_REASON_HANDSHAKE_TIMEOUT:
    case WIFI_REASON_4WAY_HANDSHAKE_TIMEOUT:return "Wi-Fi 认证超时，请检查密码、信号和路由器设置。";
    case WIFI_REASON_BEACON_TIMEOUT:return "路由器信号中断，正在重新连接。";
    default:return "尚未取得路由器 IP，正在重试；请确认名称、密码和 2.4 GHz 网络。";
  }
}
String statusJson(){
  const uint32_t now=millis();const bool connected=stationConnected();
  const char *state=!configured?"unconfigured":connected?"connected":schedule.phase==Phase::Connecting||schedule.phase==Phase::Prepare||schedule.phase==Phase::DeferredDisconnect?"connecting":"retrying";
  String message;
  if(storageError){state="error";message=localError;}
  else if(!configured)message="连接此热点后选择家里的 Wi-Fi，保存后会自动连接。";
  else if(connected)message=apActive?"已连接路由器，请记下 IP 后让电脑切回同一 Wi-Fi。":"已连接路由器。";
  else if(!localError.isEmpty())message=localError;
  else if(lastReason)message=reasonMessage(lastReason);
  else message="正在连接已保存的 Wi-Fi，请稍候。";
  String result="{\"configured\":";result+=configured?"true":"false";
  result+=",\"ssid\":"+quote(configured?String(credentials.ssid):String());
  result+=",\"station_connected\":";result+=connected?"true":"false";
  result+=",\"station_ip\":"+quote(connected?WiFi.localIP().toString():String("0.0.0.0"));
  result+=",\"state\":"+quote(state)+",\"message\":"+quote(message)+",\"reason\":"+String(lastReason);
  result+=",\"ap_active\":";result+=apActive?"true":"false";
  result+=",\"ap_shutdown_in_ms\":"+String(apActive&&schedule.apClosePending?remaining(now,schedule.apCloseAt):0);
  result+=",\"retry_in_ms\":"+String(configured&&schedule.phase==Phase::Waiting?remaining(now,schedule.nextActionAt):0)+"}";
  return result;
}
void scanResult(int count){
  struct Network {String ssid;int32_t rssi;bool secure;};std::vector<Network> networks;
  for(int i=0;i<count&&i<256;++i){String ssid=WiFi.SSID(uint8_t(i));if(ssid.isEmpty())continue;
    Network item{ssid,WiFi.RSSI(uint8_t(i)),WiFi.encryptionType(uint8_t(i))!=WIFI_AUTH_OPEN};
    auto existing=std::find_if(networks.begin(),networks.end(),[&](const Network &n){return n.ssid==ssid;});
    if(existing==networks.end())networks.push_back(item);else if(item.rssi>existing->rssi)*existing=item;
  }
  std::sort(networks.begin(),networks.end(),[](const Network &a,const Network &b){return a.rssi==b.rssi?a.ssid.compareTo(b.ssid)<0:a.rssi>b.rssi;});
  scanList="[";size_t shown=0;
  for(const auto &item:networks){if(shown==50)break;if(shown++)scanList+=',';scanList+="{\"ssid\":"+quote(item.ssid)+",\"rssi\":"+String(item.rssi)+",\"secure\":"+(item.secure?"true":"false")+"}";}
  scanList+=']';scanAvailable=true;scanRunning=false;scanMessage=count?"":"未找到可见的 2.4 GHz Wi-Fi，请靠近路由器后刷新。";WiFi.scanDelete();
}
void pollScan(uint32_t now){
  if(scanRunning){
    int result=WiFi.scanComplete();
    if(result>=0)scanResult(result);
    else if(result==WIFI_SCAN_FAILED||uint32_t(now-scanAt)>=20000){esp_wifi_scan_stop();WiFi.scanDelete();scanRunning=false;scanAvailable=true;scanList="[]";scanMessage="扫描未完成，请稍后刷新重试。";}
  }
  if(scanRequested&&!scanRunning&&schedule.canScan()){
    scanRequested=false;scanRunning=true;scanAt=now;scanAvailable=false;scanMessage="";
    int result=WiFi.scanNetworks(true,false,false,120);
    if(result>=0)scanResult(result);
    else if(result!=WIFI_SCAN_RUNNING){scanRunning=false;scanAvailable=true;scanList="[]";scanMessage="扫描启动失败，请稍后刷新重试。";}
  }
}
void requestScan(){
  bool refresh=http->hasArg("refresh")&&http->arg("refresh")=="1";
  if((refresh||!scanAvailable)&&!scanRunning)scanRequested=true;
  if(scanRequested||scanRunning){sendJson(202,"{\"scanning\":true,\"message\":\"正在扫描；正在连接时会先等待本次连接结束。\"}");return;}
  sendJson(200,"{\"scanning\":false,\"networks\":"+scanList+",\"message\":"+quote(scanMessage)+"}");
}
bool containsNul(const String &value){for(size_t i=0;i<value.length();++i)if(!value[i])return true;return false;}
void saveRequest(){
  String ssid=http->arg("ssid"),password=http->arg("password");
  if(ssid.isEmpty()||ssid.length()>32||password.length()>63||(!password.isEmpty()&&password.length()<8)||containsNul(ssid)||containsNul(password)){
    sendJson(400,"{\"error\":\"invalid_wifi_credentials\",\"message\":\"请选择 Wi-Fi；密码需为 8–63 字节，无密码网络留空。\"}");return;
  }
  Credentials next=makeCredentials(ssid,password);
  if(!saveCredentials(next)){sendJson(500,"{\"error\":\"wifi_save_failed\",\"message\":\"Wi-Fi 配置未能保存，请重试。\"}");return;}
  credentials=next;configured=true;storageError=false;localError="";lastReason=0;disconnectReason.store(0);
  scanRequested=false;schedule.saved(millis());
  sendJson(200,"{\"saved\":true,\"connecting\":true}");
}
void clearWifi(){
  Credentials empty=makeCredentials("","");
  if(!saveCredentials(empty)){storageError=true;localError="清除 Wi-Fi 配置失败，请重新配网。";openPortal();return;}
  credentials=empty;configured=false;storageError=false;schedule.phase=Phase::None;localError="";lastReason=0;disconnectReason.store(0);
  schedule.apClosePending=false;scanRequested=false;WiFi.setAutoReconnect(false);WiFi.disconnect(false,true);openPortal();
}
void portalPage(){http->sendHeader("Cache-Control","no-store");http->send_P(200,"text/html; charset=utf-8",WIFI_PORTAL_PAGE);}
void captiveRedirect(){
  if(apActive){http->sendHeader("Location","http://"+WiFi.softAPIP().toString()+"/",true);http->sendHeader("Cache-Control","no-store");http->send(302,"text/plain","");}
  else sendJson(404,"{\"error\":\"not_found\"}");
}
}

void begin(WebServer &server){
  http=&server;pinMode(ResetPin,INPUT_PULLUP);resetButton.update(digitalRead(ResetPin)==LOW,millis());
  WiFi.persistent(false);WiFi.setAutoReconnect(false);WiFi.mode(WIFI_STA);WiFi.setSleep(false);
  WiFi.onEvent([](WiFiEvent_t event,WiFiEventInfo_t info){
    if(event==ARDUINO_EVENT_WIFI_STA_DISCONNECTED&&info.wifi_sta_disconnected.reason!=WIFI_REASON_ASSOC_LEAVE)
      disconnectReason.store(info.wifi_sta_disconnected.reason,std::memory_order_relaxed);
  });
  loadCredentials();schedule.start(configured,millis());
  if(!configured)openPortal();
  http->on("/",HTTP_GET,portalPage);
  http->on("/wifi/status",HTTP_GET,[]{sendJson(200,statusJson());});
  http->on("/wifi/scan",HTTP_GET,requestScan);
  http->on("/wifi",HTTP_POST,saveRequest);
  http->on("/wifi/finish",HTTP_POST,[]{
    if(!stationConnected()){sendJson(409,"{\"error\":\"wifi_not_connected\",\"message\":\"还未连上路由器，请先完成配网。\"}");return;}
    if(apActive){schedule.apClosePending=true;schedule.apCloseAt=millis()+1000;}
    sendJson(200,"{\"accepted\":true}");
  });
  const char *probes[]={"/generate_204","/gen_204","/hotspot-detect.html","/library/test/success.html","/connecttest.txt","/ncsi.txt","/fwlink","/redirect","/canonical.html","/success.txt"};
  for(const char *path:probes)http->on(path,HTTP_GET,captiveRedirect);
  http->onNotFound([]{if(http->method()==HTTP_GET&&apActive)captiveRedirect();else sendJson(404,"{\"error\":\"not_found\"}");});
}

void poll(){
  if(!http)return;const uint32_t now=millis();
  if(resetButton.update(digitalRead(ResetPin)==LOW,now))clearWifi();
  if(apActive)dns.processNextRequest();
  uint32_t reason=disconnectReason.exchange(0,std::memory_order_relaxed);if(reason)lastReason=reason;
  pollScan(now);
  if(stationConnected()){
    if(schedule.phase!=Phase::Connected){schedule.joined(now,apActive);lastReason=0;localError="";}
    if(schedule.shouldClosePortal(apActive,now))closePortal();
    return;
  }
  if(schedule.phase==Phase::Connected)schedule.lost(now);
  if(!configured){if(!apActive)openPortal();return;}
  if(schedule.needsPortal(configured,now)&&!apActive)openPortal();
  if(schedule.attemptExpired(now)){esp_wifi_disconnect();schedule.retry(now);return;}
  if(scanRunning)return;
  if(schedule.phase==Phase::DeferredDisconnect&&due(now,schedule.nextActionAt)){
    openPortal();esp_wifi_disconnect();schedule.disconnected(now);return;
  }
  if(schedule.canStart(now,scanRunning,scanRequested)){
    // SDK may do one first-failure retry even with autoReconnect disabled. We
    // never overlap it with another begin within the 30-second attempt window.
    if(WiFi.status()==WL_CONNECTED){esp_wifi_disconnect();schedule.nextActionAt=now+300;return;}
    WiFi.setAutoReconnect(false);lastReason=0;disconnectReason.store(0);localError="";
    bool prepared=WiFi.STA.begin()&&WiFi.STA.connect(credentials.ssid,credentials.password,0,nullptr,false);
    if(!prepared||esp_wifi_connect()!=ESP_OK){
      schedule.retry(now);localError="Wi-Fi 连接启动失败，稍后自动重试。";
    }else schedule.connecting(now);
  }
}

void appendStatus(String &json){
  const bool connected=stationConnected();
  json+=",\"ip\":"+quote(apActive?WiFi.softAPIP().toString():String("0.0.0.0"));
  json+=",\"station_ip\":"+quote(connected?WiFi.localIP().toString():String("0.0.0.0"));
  json+=",\"station_connected\":";json+=connected?"true":"false";
  json+=",\"wifi\":"+statusJson();
}
}
