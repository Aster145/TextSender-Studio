// SPDX-License-Identifier: MIT
// Copyright (c) 2026 TextSender Studio contributors
#pragma once
#include <Arduino.h>

static const char WIFI_PORTAL_PAGE[] PROGMEM = R"PORTAL(<!doctype html>
<!--
Refactoring UI constraint brief: plain HTML, no framework or external assets.
Primary: choose a scanned Wi-Fi and connect; after success, read the device IP.
Secondary: password, connection progress/failure; tertiary: rescan and recovery.
One filled action in the visible panel. Structure uses labels/weight/spacing first.
Existing Studio tokens: page F6FBFD, panel FAFDFF, text 222435, muted 5C6479,
line D3DEE4, accent 0C5D77, soft EBFFFF. Spacing 4/8/12/16/24/32/48;
type 12/14/16/20/24/32; weights 400/600; radius 8; controls at least 44px.
-->
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="color-scheme" content="light">
<title>Wi-Fi 配网 · TextSender Studio</title>
<style>
:root{--page:#f6fbfd;--panel:#fafdff;--text:#222435;--muted:#5c6479;--line:#d3dee4;--accent:#0c5d77;--soft:#ebffff;color-scheme:light}
*{box-sizing:border-box}body{margin:0;background:var(--page);color:var(--text);font:400 16px/1.6 system-ui,-apple-system,"Segoe UI","Microsoft YaHei",sans-serif}main{max-width:560px;margin:48px auto;padding:0 24px}h1,h2,p{margin:0}h1{font-size:32px;line-height:1.25;font-weight:600;margin:24px 0 12px;letter-spacing:-.5px}h2{font-size:20px;line-height:1.4;font-weight:600}button,input,select{font:inherit}button,input,select{border-radius:8px}button{min-height:44px;padding:8px 16px;background:var(--panel);color:var(--text);border:1px solid var(--line);font-weight:600;cursor:pointer}button:hover{border-color:var(--accent)}button:disabled{cursor:wait;color:var(--muted);background:var(--page)}button.primary{background:var(--accent);color:var(--panel);border-color:var(--accent)}button.primary:disabled{opacity:.6}button.link{padding:8px 0;border:0;background:transparent;color:var(--accent);font-weight:400;text-align:left}input:not([type=checkbox]),select{display:block;width:100%;min-height:48px;padding:8px 12px;border:1px solid var(--line);background:var(--panel);color:var(--text)}input[type=checkbox]{width:20px;height:20px;accent-color:var(--accent);margin:0;flex-shrink:0}input:focus-visible,select:focus-visible,button:focus-visible,summary:focus-visible{outline:3px solid var(--accent);outline-offset:3px}[hidden]{display:none!important}.brand{display:flex;align-items:center;gap:12px;font-weight:600;font-size:14px}.mark{display:grid;place-items:center;width:32px;height:32px;background:var(--soft);border-radius:8px;color:var(--accent)}.brand span:last-child{font-size:12px;font-weight:400;color:var(--muted)}.intro,.muted,.help{color:var(--muted)}.intro{max-width:32em}.card{margin-top:24px;padding:24px;background:var(--panel);border:1px solid var(--line);border-radius:8px}.group{margin-bottom:24px}.group-label{display:block;font-weight:600;margin-bottom:8px}.label-row{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-bottom:4px}.label-row label{font-weight:600}.label-row button{font-size:14px}.help{font-size:14px;margin-top:8px;overflow-wrap:anywhere}.check{display:flex;align-items:center;gap:8px;min-height:44px;font-size:14px}.password-check{margin-top:4px}.full{width:100%}.note{margin-top:16px;padding:12px 16px;border-left:4px solid var(--accent);background:var(--soft);color:var(--accent);border-radius:0 8px 8px 0;font-size:14px;overflow-wrap:anywhere}.note strong{display:block;font-size:16px;margin-bottom:4px}.status-title{font-weight:600}.subtle-note{margin-top:16px;font-size:14px;color:var(--muted)}.network-name{font-weight:600;overflow-wrap:anywhere}.status-dot{display:inline-block;width:8px;height:8px;border-radius:50%;background:var(--accent);margin-right:8px}.success-header{color:var(--accent);margin-bottom:24px}.success-header p{font-size:14px;margin-top:8px}.ip-label{display:block;font-size:14px;color:var(--muted);margin-bottom:4px}.ip-row{display:flex;gap:8px;align-items:center}.ip-row input{font-size:24px;font-weight:600;letter-spacing:.5px;font-variant-numeric:tabular-nums;min-width:0;border:0;background:var(--page);padding:12px}.ip-row button{flex-shrink:0;font-size:14px}.port{font-size:14px;color:var(--muted);margin:8px 0 24px}.success-instructions{padding-left:24px;margin:0 0 24px}.success-instructions li+li{margin-top:8px}.countdown{font-size:14px;color:var(--muted);margin-top:12px}.copy-result{font-size:14px;min-height:24px;color:var(--accent);margin-top:4px}details{margin-top:16px}summary{color:var(--accent);cursor:pointer;min-height:44px;display:list-item;line-height:44px;font-size:14px}details .manual-fields{margin-top:12px}.divider{border:0;border-top:1px solid var(--line);margin:24px 0}footer{font-size:12px;color:var(--muted);margin:24px 0 32px;line-height:1.8}footer p+p{margin-top:8px}footer a{color:var(--accent)}.empty{padding:16px;background:var(--page);border-radius:8px;color:var(--muted);font-size:14px}.connection-line{font-size:14px;color:var(--muted);margin-top:16px;overflow-wrap:anywhere}noscript{display:block;margin-top:16px}.busy-label{display:inline-flex;align-items:center;gap:8px}.spinner{width:16px;height:16px;border:2px solid var(--line);border-top-color:var(--accent);border-radius:50%;animation:spin 1s linear infinite}@keyframes spin{to{transform:rotate(360deg)}}@media(prefers-reduced-motion:reduce){.spinner{animation:none}}@media(max-width:480px){main{margin:24px auto;padding:0 16px}h1{font-size:24px}.card{padding:16px}.brand{gap:8px}.ip-row input{font-size:20px;letter-spacing:0}.ip-row button{padding:8px 12px}}
</style>
<style>
.success-header{text-align:center}.success-mark{display:grid;place-items:center;width:64px;height:64px;margin:0 auto 16px;border-radius:50%;background:var(--soft);color:var(--accent)}.manage-row{margin-top:16px}.manage-row input{font-size:16px;font-weight:400;letter-spacing:0}.countdown{padding:12px 16px;border-radius:8px;background:var(--soft);color:var(--accent)}
</style>
</head>
<body>
<main>
 <header>
  <div class="brand"><span class="mark" aria-hidden="true"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M3 8a14 14 0 0 1 18 0M6 12a9 9 0 0 1 12 0M9 16a4 4 0 0 1 6 0"/><circle cx="12" cy="20" r="1" fill="currentColor" stroke="none"/></svg></span>TextSender Studio<span>设备配网</span></div>
  <h1 id="pageTitle">连接你的 Wi-Fi</h1>
  <p class="intro" id="pageIntro">选择家里的 Wi-Fi，输入密码。保存后，设备每次上电都会自动连接。</p>
 </header>
 <div id="notice" class="note" role="status" aria-live="polite" hidden><strong id="noticeTitle"></strong><span id="noticeText"></span></div>
 <section id="setupPanel" class="card" aria-label="Wi-Fi 配置">
  <form id="wifiForm" method="post" action="/wifi">
   <div class="group">
    <div class="label-row"><label for="network">附近的 Wi-Fi</label><button id="refresh" type="button" class="link">重新搜索</button></div>
    <select id="network" name="ssid" aria-describedby="scanMessage"><option value="">正在搜索附近的 Wi-Fi…</option></select>
    <p id="scanMessage" class="help" aria-live="polite"><span class="busy-label"><span class="spinner" aria-hidden="true"></span>正在搜索，请稍等。</span></p>
    <div id="noNetworks" class="empty" hidden>暂未找到 Wi-Fi。请靠近路由器，确认已开启 2.4 GHz Wi-Fi，然后重新搜索。</div>
    <details id="manual"><summary>没有找到？手动输入隐藏网络</summary><div class="manual-fields"><label class="group-label" for="manualSsid">Wi-Fi 名称</label><input id="manualSsid" type="text" maxlength="32" autocomplete="off" autocapitalize="none" spellcheck="false" placeholder="准确填写 Wi-Fi 名称"><label class="check"><input type="checkbox" id="manualOpen">此网络不需要密码</label></div></details>
   </div>
   <div id="passwordGroup" class="group">
    <label class="group-label" for="password">Wi-Fi 密码</label>
    <input id="password" name="password" type="password" autocomplete="new-password" autocapitalize="none" spellcheck="false" placeholder="输入所选 Wi-Fi 的密码" aria-describedby="passwordHelp">
    <label class="check password-check"><input type="checkbox" id="showPassword">显示密码</label>
    <p id="passwordHelp" class="help">密码只保存在开发板上。</p>
   </div>
   <p id="openNetwork" class="help group" hidden>这是开放网络，无需填写密码。</p>
   <button id="connect" class="primary full" type="submit">保存并连接 Wi-Fi</button>
   <p id="savedNetwork" class="connection-line" hidden></p>
  </form>
 </section>
 <section id="successPanel" class="card" aria-label="已连接" hidden>
  <div class="success-header"><div class="success-mark" aria-hidden="true"><svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m5 12 4 4L19 6"/></svg></div><h2>配网成功</h2><p>已连接 Wi-Fi：<span id="connectedSsid" class="network-name"></span></p></div>
  <label for="stationIp" class="ip-label">开发板 IP 地址</label>
  <div class="ip-row"><input id="stationIp" type="text" readonly aria-label="开发板 IP 地址"><button id="copyIp" type="button">复制 IP</button></div>
  <div class="manage-row"><label for="manageUrl" class="ip-label">局域网管理网址</label><div class="ip-row"><input id="manageUrl" type="text" readonly aria-label="局域网管理网址"><button id="copyUrl" type="button">复制网址</button></div></div>
  <p id="copyResult" class="copy-result" role="status" aria-live="polite"></p>
  <p class="port">切回同一 Wi-Fi 后可访问管理页。程序中的 ESP32 端口填写 <strong>80</strong>。</p>
  <ol class="success-instructions"><li>先记下上面的 IP 地址。</li><li>让电脑连接同一个 Wi-Fi，在 TextSender Studio 设置中填写这个 IP。</li></ol>
  <p id="countdown" class="countdown"></p>
  <button id="finish" type="button" class="primary full" style="margin-top:16px">我已记下信息，结束配网</button>
  <button id="changeWifi" type="button" class="link">更换 Wi-Fi</button>
 </section>
 <noscript class="note">请使用启用了 JavaScript 的浏览器打开 http://192.168.4.1，以搜索附近 Wi-Fi 并完成配置。</noscript>
 <footer><p>设备支持 2.4 GHz Wi-Fi。配网热点 <strong>TextSender-Setup</strong> 无需密码。</p><p>需要重新配网时，设备正常启动后长按 <strong>BOOT 5 秒</strong>。RESET / EN 只用于重启。</p><p>引导页未弹出时，连接配网热点后打开 <a href="http://192.168.4.1/">192.168.4.1</a>。</p></footer>
</main>
<script>
(function(){
'use strict';
var $=function(id){return document.getElementById(id);};
var network=$('network'), password=$('password'), manual=$('manual');
var networks=[], statusTimer=0, scanTimer=0, scanBusy=false, statusBusy=false;
var submitting=false, finishing=false, ended=false, editing=false, connected=false;
var pendingSsid='', pendingAt=0, generation=0, lastStatus=null, lastIp='', offline=false;
var terminalFormError=false;
function note(title,text){$('noticeTitle').textContent=title;$('noticeText').textContent=text;$('notice').hidden=false;}
function hideNote(){$('notice').hidden=true;}
function bytes(text){return unescape(encodeURIComponent(text)).length;}
function validIp(value){return typeof value==='string'&&/^(\d{1,3}\.){3}\d{1,3}$/.test(value)&&value.split('.').every(function(n){return Number(n)<=255;})&&value!=='0.0.0.0'&&value!=='255.255.255.255';}
function selectedSsid(){return manual.open?$('manualSsid').value:network.value;}
function isOpen(){if(manual.open)return $('manualOpen').checked;var item=networks.find(function(n){return n.ssid===network.value;});return !!item&&!item.secure;}
function updatePassword(){var open=isOpen();$('passwordGroup').hidden=open;$('openNetwork').hidden=!open;password.disabled=open||submitting;if(open){password.value='';$('showPassword').checked=false;password.type='password';}}
function controls(){var waiting=!!pendingSsid&&Date.now()-pendingAt<35000;var locked=submitting||waiting;network.disabled=locked;password.disabled=locked||isOpen();$('manualSsid').disabled=locked;$('manualOpen').disabled=locked;$('refresh').disabled=locked||scanBusy;$('connect').disabled=locked;$('connect').textContent=submitting?'正在保存…':waiting?'正在连接 Wi-Fi…':'保存并连接 Wi-Fi';}
async function request(path,options){
 var controller=new AbortController(),timer=setTimeout(function(){controller.abort();},8000);
 try{var response=await fetch(path,Object.assign({cache:'no-store',signal:controller.signal},options||{}));var data;try{data=await response.json();}catch(e){throw new Error('设备响应无法读取，请稍后重试。');}if(!response.ok&&response.status!==202){throw new Error(data.message||({'invalid_wifi_credentials':'请检查 Wi-Fi 名称和密码长度。','wifi_save_failed':'设备未能保存配置，请重新上电后重试。','not_connected':'尚未连接 Wi-Fi，请等待连接完成。'}[data.error])||'设备暂时无法处理，请稍后重试。');}return data;}
 finally{clearTimeout(timer);}
}
function scheduleStatus(delay){clearTimeout(statusTimer);if(!ended)statusTimer=setTimeout(checkStatus,delay);}
function renderStatus(s){
 lastStatus=s;connected=s.station_connected===true&&validIp(s.station_ip);
 if(s.configured&&s.ssid){$('savedNetwork').textContent='已保存网络：'+s.ssid;$('savedNetwork').hidden=false;}else $('savedNetwork').hidden=true;
 if(pendingSsid&&(s.state==='error'||s.state==='retrying')){pendingSsid='';terminalFormError=false;}
 if(pendingSsid&&Date.now()-pendingAt>=35000){pendingSsid='';}
 var pendingMatch=!pendingSsid||(s.ssid===pendingSsid&&Date.now()-pendingAt>=1200);
 if(connected&&s.state==='connected'&&pendingMatch&&!submitting){
  if(pendingSsid){pendingSsid='';editing=false;password.value='';}
  lastIp=s.station_ip;$('stationIp').value=lastIp;$('manageUrl').value='http://'+lastIp+'/';$('connectedSsid').textContent=s.ssid||'已连接的 Wi-Fi';
  $('setupPanel').hidden=!editing;$('successPanel').hidden=editing;
  $('pageTitle').textContent=editing?'更换设备的 Wi-Fi':'连接成功';
  $('pageIntro').textContent=editing?'选择新的 Wi-Fi，填写密码后保存。':'设备已加入路由器。记下 IP，就可以从电脑控制设备。';
  if(!editing){hideNote();$('finish').hidden=!s.ap_active;$('countdown').textContent=s.ap_active?'配网热点将在约 '+Math.max(1,Math.ceil((s.ap_shutdown_in_ms||0)/1000))+' 秒后关闭。也可以记下 IP 后立即结束配网。':'配网热点已关闭。设备下次上电会自动连接此 Wi-Fi。';}
 }else{
  $('setupPanel').hidden=false;$('successPanel').hidden=true;$('pageTitle').textContent='连接你的 Wi-Fi';$('pageIntro').textContent='选择家里的 Wi-Fi，输入密码。保存后，设备每次上电都会自动连接。';
  if(!terminalFormError){if(s.state==='connecting'||pendingSsid)note('正在连接',s.message||'正在连接路由器并获取 IP，请稍等。');else if(s.state==='retrying'||s.state==='error')note('尚未连接成功',s.message||'请确认密码及路由器状态，然后重新保存。');else if(!s.configured)hideNote();}
 }
 controls();
}
async function checkStatus(){
 if(ended||statusBusy)return;if(document.hidden){scheduleStatus(2500);return;}
 statusBusy=true;var current=generation;
 try{var s=await request('/wifi/status');if(current===generation&&!ended){offline=false;renderStatus(s);}}
 catch(e){if(!ended&&current===generation){offline=true;if(lastIp&&!editing){note('配网页面已断开','配网热点可能已关闭。请切回家里的 Wi-Fi，并使用已显示的开发板 IP。');}else note('暂时无法连接开发板','请保持连接 TextSender-Setup 热点。网络切换可能短暂断开，页面会自动重试。');}}
 finally{statusBusy=false;scheduleStatus(offline?3000:1500);}
}
function renderNetworks(list){
 var previous=network.value;networks=Array.isArray(list)?list.filter(function(n){return n&&typeof n.ssid==='string'&&n.ssid.length>0;}):[];
 network.replaceChildren();var prompt=document.createElement('option');prompt.value='';prompt.textContent=networks.length?'请选择 Wi-Fi':'未找到 Wi-Fi';network.appendChild(prompt);
 networks.forEach(function(n){var item=document.createElement('option');item.value=n.ssid;item.textContent=n.ssid+' · '+(n.secure?'需密码':'无密码')+' · '+(n.rssi>=-55?'信号强':n.rssi>=-75?'信号一般':'信号较弱');network.appendChild(item);});
 if(networks.some(function(n){return n.ssid===previous;}))network.value=previous;
 $('noNetworks').hidden=networks.length>0;$('scanMessage').textContent=networks.length?'找到 '+networks.length+' 个 Wi-Fi，选择后填写密码。':'也可以展开下方选项，手动输入隐藏网络。';updatePassword();
}
async function scan(refresh){
 if(scanBusy||ended||submitting||pendingSsid)return;clearTimeout(scanTimer);scanBusy=true;controls();$('scanMessage').textContent='正在搜索附近的 Wi-Fi…';
 try{var data=await request('/wifi/scan'+(refresh?'?refresh=1':''));if(data.scanning){scanTimer=setTimeout(function(){scan(false);},1200);}else{renderNetworks(data.networks);if(data.message)$('scanMessage').textContent=data.message;}}
 catch(e){$('scanMessage').textContent=e.message||'搜索失败，请重新搜索。';}
 finally{scanBusy=false;controls();}
}
$('wifiForm').addEventListener('submit',async function(event){
 event.preventDefault();if(submitting||pendingSsid||ended)return;var ssid=selectedSsid(),pass=isOpen()?'':password.value;
 terminalFormError=true;
 if(!ssid){note('请选择 Wi-Fi','从列表中选择附近的 Wi-Fi，或展开选项填写隐藏网络名称。');return;}
 if(bytes(ssid)>32){note('Wi-Fi 名称过长','Wi-Fi 名称最多 32 字节，请检查输入。');return;}
 if(!isOpen()&&(bytes(pass)<8||bytes(pass)>63)){note('请检查 Wi-Fi 密码','密码需为 8–63 字节。若隐藏网络没有密码，请勾选“此网络不需要密码”。');password.focus();return;}
 generation++;submitting=true;editing=true;terminalFormError=false;clearTimeout(scanTimer);controls();note('正在保存配置','保存成功后，设备会尝试连接所选 Wi-Fi。');
 try{await request('/wifi',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:new URLSearchParams({ssid:ssid,password:pass}).toString()});pendingSsid=ssid;pendingAt=Date.now();password.value='';note('配置已保存','正在连接 '+ssid+'，成功后会显示开发板 IP 地址。');}
 catch(e){terminalFormError=true;note('尚未确认保存成功',e.name==='AbortError'?'请求超时，请查看连接状态后再重试。':e.message);}
 finally{submitting=false;controls();scheduleStatus(1200);}
});
network.addEventListener('change',function(){manual.open=false;terminalFormError=false;password.value='';updatePassword();});
manual.addEventListener('toggle',function(){updatePassword();});
$('manualOpen').addEventListener('change',updatePassword);
$('showPassword').addEventListener('change',function(){password.type=this.checked?'text':'password';});
$('refresh').addEventListener('click',function(){scan(true);});
async function copyValue(input,label){
 var copied=false;try{if(navigator.clipboard&&window.isSecureContext){await navigator.clipboard.writeText(input.value);copied=true;}}catch(e){}
 if(!copied){input.focus();input.select();input.setSelectionRange(0,input.value.length);try{copied=document.execCommand('copy');}catch(e){}}
 $('copyResult').textContent=copied?label+'已复制。':'已选中'+label+'，请长按或按 Ctrl+C 复制。';
}
$('copyIp').addEventListener('click',function(){copyValue($('stationIp'),'IP');});
$('copyUrl').addEventListener('click',function(){copyValue($('manageUrl'),'管理网址');});
$('finish').addEventListener('click',async function(){
 if(finishing||!connected||ended)return;finishing=true;this.disabled=true;generation++;clearTimeout(statusTimer);clearTimeout(scanTimer);
 try{await request('/wifi/finish',{method:'POST'});ended=true;hideNote();$('finish').hidden=true;$('changeWifi').hidden=true;$('countdown').textContent='配网已结束，请切回家里的 Wi-Fi。设备将保留并自动使用已保存的配置。';}
 catch(e){note('请确认网络连接','热点可能已经关闭。请切回家里的 Wi-Fi；上面的 IP 已保留。如果仍连接配网热点，可以再试一次。');scheduleStatus(2500);}
 finally{finishing=false;this.disabled=false;}
});
$('changeWifi').addEventListener('click',function(){editing=true;pendingSsid='';terminalFormError=false;hideNote();if(lastStatus)renderStatus(lastStatus);scan(true);});
document.addEventListener('visibilitychange',function(){if(!document.hidden&&!ended){scheduleStatus(0);if(!networks.length&&!scanBusy&&!pendingSsid)scan(false);}});
window.addEventListener('online',function(){if(!ended)scheduleStatus(0);});
updatePassword();checkStatus();scan(false);
})();
</script>
</body>
</html>)PORTAL";
