// SPDX-License-Identifier: MIT
// Copyright (c) 2026 TextSender Studio contributors
// HID class request/endpoint handling adapted directly from TinyUSB's MIT
// hid_device.c, Copyright (c) 2019 Ha Thach (tinyusb.org).
// See ../NOTICE.md and ../TINYUSB-LICENSE.txt.
#include "StudioUsb.h"
#include "SwitchDescriptor.h"
#include "esp32-hal-tinyusb.h"
#include "device/usbd_pvt.h"
#include <atomic>

namespace {
struct HidPort {
  uint8_t number=0, input=0, output=0, idle=0, protocol=1;
  const tusb_hid_descriptor_hid_t *descriptor=nullptr;
  alignas(4) uint8_t incoming[64]{}, outgoing[64]{}, control[64]{}, cached[8]{};
  SemaphoreHandle_t complete=nullptr, owner=nullptr;
  std::atomic<bool> success{false};
  uint32_t lastWrite=0;
};
HidPort ports[2];
std::atomic<bool> plugged{true};
std::atomic<uint8_t> keyboardLeds{0};
uint32_t liveSince=0;
bool previouslyLive=false;
const uint8_t keyboardDefinition[]={TUD_HID_REPORT_DESC_KEYBOARD()};
const uint8_t idlePad[]={0,0,15,128,128,128,128,0};

HidPort *interfacePort(uint8_t n){for(auto &p:ports)if(p.number==n)return &p;return nullptr;}
void resetDevice(uint8_t){
  for(unsigned i=0;i<2;i++){
    auto &p=ports[i];p.input=p.output=p.idle=0;p.protocol=1;p.descriptor=nullptr;p.success=false;
    memset(p.cached,0,8);if(i==0)memcpy(p.cached,idlePad,8);
  }
  previouslyLive=false;keyboardLeds=0;
}
void initialize(){resetDevice(0);}
bool finish(){resetDevice(0);return true;}
uint16_t openInterface(uint8_t root,const tusb_desc_interface_t *itf,uint16_t room){
  if(itf->bInterfaceClass!=TUSB_CLASS_HID||itf->bNumEndpoints!=2)return 0;
  uint16_t size=sizeof(tusb_desc_interface_t)+sizeof(tusb_hid_descriptor_hid_t)+2*sizeof(tusb_desc_endpoint_t);
  auto *p=interfacePort(itf->bInterfaceNumber);
  if(!p||room<size)return 0;
  auto *descriptor=reinterpret_cast<const tusb_hid_descriptor_hid_t*>(tu_desc_next(itf));
  if(descriptor->bDescriptorType!=HID_DESC_TYPE_HID)return 0;
  if(!usbd_open_edpt_pair(root,tu_desc_next(descriptor),2,TUSB_XFER_INTERRUPT,&p->output,&p->input))return 0;
  p->descriptor=descriptor;
  if(!usbd_edpt_xfer(root,p->output,p->incoming,sizeof(p->incoming),false))return 0;
  return size;
}
bool controlRequest(uint8_t root,uint8_t phase,const tusb_control_request_t *r){
  if(r->bmRequestType_bit.recipient!=TUSB_REQ_RCPT_INTERFACE)return false;
  auto *p=interfacePort(uint8_t(r->wIndex));if(!p)return false;
  const bool keyboard=p==&ports[1];
  if(r->bmRequestType_bit.type==TUSB_REQ_TYPE_STANDARD){
    if(phase!=CONTROL_STAGE_SETUP)return true;
    if(r->bRequest!=TUSB_REQ_GET_DESCRIPTOR)return false;
    if(tu_u16_high(r->wValue)==HID_DESC_TYPE_HID&&p->descriptor)
      return tud_control_xfer(root,r,(void*)p->descriptor,sizeof(tusb_hid_descriptor_hid_t));
    if(tu_u16_high(r->wValue)==HID_DESC_TYPE_REPORT)
      return tud_control_xfer(root,r,(void*)(keyboard?keyboardDefinition:switchDescriptor),keyboard?sizeof(keyboardDefinition):sizeof(switchDescriptor));
    return false;
  }
  if(r->bmRequestType_bit.type!=TUSB_REQ_TYPE_CLASS)return false;
  if(r->bRequest==HID_REQ_CONTROL_SET_REPORT){
    if(r->wLength>sizeof(p->control)||tu_u16_low(r->wValue)!=0)return false;
    if(phase==CONTROL_STAGE_SETUP)return tud_control_xfer(root,r,p->control,r->wLength);
    if(phase==CONTROL_STAGE_ACK&&keyboard&&r->wLength&&tu_u16_high(r->wValue)==HID_REPORT_TYPE_OUTPUT)keyboardLeds=p->control[0];
    return true;
  }
  if(phase!=CONTROL_STAGE_SETUP)return true;
  switch(r->bRequest){
    case HID_REQ_CONTROL_GET_REPORT:
      if(tu_u16_low(r->wValue)!=0)return false;
      if(tu_u16_high(r->wValue)==HID_REPORT_TYPE_INPUT){memcpy(p->control,p->cached,8);return tud_control_xfer(root,r,p->control,8);}
      if(keyboard&&tu_u16_high(r->wValue)==HID_REPORT_TYPE_OUTPUT){p->control[0]=keyboardLeds;return tud_control_xfer(root,r,p->control,1);}return false;
    case HID_REQ_CONTROL_GET_IDLE:p->control[0]=p->idle;return tud_control_xfer(root,r,p->control,1);
    case HID_REQ_CONTROL_SET_IDLE:p->idle=tu_u16_high(r->wValue);return tud_control_status(root,r);
    case HID_REQ_CONTROL_GET_PROTOCOL:p->control[0]=p->protocol;return tud_control_xfer(root,r,p->control,1);
    case HID_REQ_CONTROL_SET_PROTOCOL:if(r->wValue>1)return false;p->protocol=r->wValue;return tud_control_status(root,r);
    default:return false;
  }
}
bool completed(uint8_t root,uint8_t endpoint,xfer_result_t result,uint32_t count){
  for(auto &p:ports){
    if(endpoint==p.input&&p.input){p.success=result==XFER_RESULT_SUCCESS&&count==8;xSemaphoreGive(p.complete);return true;}
    if(endpoint==p.output&&p.output){
      if(&p==&ports[1]&&result==XFER_RESULT_SUCCESS&&count)keyboardLeds=p.incoming[0];
      return usbd_edpt_xfer(root,p.output,p.incoming,sizeof(p.incoming),false);
    }
  }return false;
}
const usbd_class_driver_t driver={.name="Studio HID",.init=initialize,.deinit=finish,.reset=resetDevice,.open=openInterface,.control_xfer_cb=controlRequest,.xfer_cb=completed,.xfer_isr=nullptr,.sof=nullptr};
uint16_t descriptors(uint8_t *out,uint8_t *interfaceNumber){
  uint16_t used=0;
  for(unsigned i=0;i<2;i++){
    ports[i].number=(*interfaceNumber)++;
    const uint8_t text=tinyusb_add_string_descriptor(i?"TextSender Keyboard":"TextSender Gamepad");
    const uint8_t endpoint=i+1;
    const uint8_t bytes[]={TUD_HID_INOUT_DESCRIPTOR(ports[i].number,text,i?HID_ITF_PROTOCOL_KEYBOARD:HID_ITF_PROTOCOL_NONE,i?sizeof(keyboardDefinition):sizeof(switchDescriptor),endpoint,uint8_t(0x80|endpoint),64,1)};
    memcpy(out+used,bytes,sizeof(bytes));used+=sizeof(bytes);
  }return used;
}
}
extern "C" const usbd_class_driver_t *usbd_app_driver_get_cb(uint8_t *count){*count=1;return &driver;}
namespace studioUsb {
bool start(){
  for(auto &p:ports){p.complete=xSemaphoreCreateBinary();p.owner=xSemaphoreCreateMutex();if(!p.complete||!p.owner)return false;}
  return tinyusb_enable_interface(USB_INTERFACE_CUSTOM,2*TUD_HID_INOUT_DESC_LEN,descriptors)==ESP_OK;
}
bool enabled(){return plugged;}
bool available(){return plugged&&tud_mounted()&&!tud_suspended()&&ports[0].input&&ports[1].input;}
bool readyForInput(){return previouslyLive&&available()&&uint32_t(millis()-liveSince)>=1000;}
uint8_t leds(){return keyboardLeds;}
bool write(Device device,const uint8_t packet[8],uint32_t timeout){
  auto &p=ports[device];if(!p.owner||xSemaphoreTake(p.owner,pdMS_TO_TICKS(timeout))!=pdTRUE)return false;
  bool ok=false;const uint32_t start=millis();
  while(available()&&usbd_edpt_busy(0,p.input)&&uint32_t(millis()-start)<timeout)vTaskDelay(1);
  if(available()&&usbd_edpt_claim(0,p.input)){
    xSemaphoreTake(p.complete,0);p.success=false;memcpy(p.outgoing,packet,8);
    if(usbd_edpt_xfer(0,p.input,p.outgoing,8,false)){
      ok=xSemaphoreTake(p.complete,pdMS_TO_TICKS(timeout))==pdTRUE&&p.success;
      if(ok){memcpy(p.cached,packet,8);p.lastWrite=millis();}
    }else usbd_edpt_release(0,p.input);
  }
  xSemaphoreGive(p.owner);return ok;
}
bool enable(bool value){
  if(value==plugged)return true;
  previouslyLive=false;
  if(!value){plugged=false;return tud_disconnect();}
  resetDevice(0);plugged=true;return tud_connect();
}
void poll(){
  if(available()){if(!previouslyLive){previouslyLive=true;liveSince=millis();}}
  else previouslyLive=false;
  auto &k=ports[1];if(k.idle&&available()&&uint32_t(millis()-k.lastWrite)>=uint32_t(k.idle)*4){uint8_t bytes[8];memcpy(bytes,k.cached,8);write(Keys,bytes);}
}
}
