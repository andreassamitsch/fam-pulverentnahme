'use strict';

let authenticatedPersonnel=null,personnelAuthLoading=false,personnelAuthConfigured=true,personnelAuthHttps=location.protocol==='https:';
let personnelIdleTimeoutMinutes=480,personnelLastUserActivityAt=0,personnelIdleTimer=null,personnelActivityPingAt=0;
const PERSONNEL_ACTIVITY_PING_MIN_MS=30000;

function personnelSessionMatchesSelection(){return Boolean(authenticatedPersonnel&&selectedPersonnel&&authenticatedPersonnel.personnelNo===selectedPersonnel.personnelNo&&authenticatedPersonnel.fullName===selectedPersonnel.fullName)}
function resetPersonnelPassword(){const e=$('personnelPassword');if(e)e.value=''}
function authTransportSuffix(){return personnelAuthHttps?'':' · HTTP-Testbetrieb'}
function notifyWorkerFlow(){if(typeof window.refreshWorkerFlow==='function')window.refreshWorkerFlow()}
function normalizePersonnelIdleTimeout(value){const n=Number(value);return Number.isFinite(n)&&n>=5&&n<=1440?Math.round(n):480}
function clearPersonnelIdleTimer(){if(personnelIdleTimer){clearTimeout(personnelIdleTimer);personnelIdleTimer=null}}
function setPersonnelIdleTimeout(value){personnelIdleTimeoutMinutes=normalizePersonnelIdleTimeout(value);if(personnelSessionMatchesSelection())schedulePersonnelIdleTimer()}
function schedulePersonnelIdleTimer(remainingMs=null){
  clearPersonnelIdleTimer();
  if(!personnelSessionMatchesSelection())return;
  const timeoutMs=personnelIdleTimeoutMinutes*60000;
  const delay=remainingMs==null
    ?Math.max(0,(personnelLastUserActivityAt||Date.now())+timeoutMs-Date.now())
    :Math.max(0,Number(remainingMs)||0);
  personnelIdleTimer=setTimeout(()=>{
    personnelIdleTimer=null;
    if(!personnelSessionMatchesSelection())return;
    const idleFor=Date.now()-(personnelLastUserActivityAt||Date.now());
    if(remainingMs!=null||idleFor>=timeoutMs-250){
      logoutPersonnel('idle').catch(()=>{});
      return;
    }
    schedulePersonnelIdleTimer();
  },Math.min(delay,2147483000));
}
function applySessionTiming(body,{freshActivity=false}={}){
  setPersonnelIdleTimeout(body?.idleTimeoutMinutes??window.FamUiConfig?.personnelIdleTimeoutMinutes??480);
  if(freshActivity){
    personnelLastUserActivityAt=Date.now();
    personnelActivityPingAt=Date.now();
    schedulePersonnelIdleTimer();
    return;
  }
  const remainingSeconds=Number(body?.idleRemainingSeconds);
  if(Number.isFinite(remainingSeconds)&&remainingSeconds>=0){
    const timeoutMs=personnelIdleTimeoutMinutes*60000;
    const remainingMs=Math.min(timeoutMs,remainingSeconds*1000);
    personnelLastUserActivityAt=Date.now()-(timeoutMs-remainingMs);
    schedulePersonnelIdleTimer(remainingMs);
  }else{
    personnelLastUserActivityAt=Date.now();
    schedulePersonnelIdleTimer();
  }
}
function clearPersonnelIdleTracking(){clearPersonnelIdleTimer();personnelLastUserActivityAt=0;personnelActivityPingAt=0}
function expirePersonnelLocally(message='Wegen Inaktivität automatisch abgemeldet. Bitte erneut per NFC oder mit Passwort anmelden.'){
  authenticatedPersonnel=null;resetPersonnelPassword();clearPersonnelIdleTracking();
  if(selectedPersonnel){showManualPersonnelLogin();$('personnelStatus').className='status bad';$('personnelStatus').textContent='⛔ '+message}
  updateBookState();renderSummary();notifyWorkerFlow();
}
async function pingPersonnelActivity(){
  if(!personnelSessionMatchesSelection())return;
  const now=Date.now();
  if(now-personnelActivityPingAt<PERSONNEL_ACTIVITY_PING_MIN_MS)return;
  personnelActivityPingAt=now;
  const r=await api('/api/personnel/activity',{method:'POST',body:'{}'});
  if(!r.ok||r.body?.authenticated!==true){
    expirePersonnelLocally();
    return;
  }
  setPersonnelIdleTimeout(r.body?.idleTimeoutMinutes);
}
function registerPersonnelUserActivity(){
  const activity=()=>{
    if(!personnelSessionMatchesSelection())return;
    personnelLastUserActivityAt=Date.now();
    schedulePersonnelIdleTimer();
    pingPersonnelActivity().catch(()=>{});
  };
  document.addEventListener('pointerdown',activity,true);
  document.addEventListener('keydown',activity,true);
  document.addEventListener('touchstart',activity,{capture:true,passive:true});
  document.addEventListener('visibilitychange',()=>{
    if(document.visibilityState!=='visible'||!personnelSessionMatchesSelection())return;
    api('/api/personnel/session').then(r=>{
      if(!r.ok||!r.body?.authenticated){expirePersonnelLocally();return}
      applySessionTiming(r.body);
    }).catch(()=>{});
  });
}
function showManualPersonnelLogin(){
  const box=$('personnelLogin');if(box)box.classList.toggle('hidden',!selectedPersonnel);
  $('personnelLogoutBtn')?.classList.add('hidden');$('personnelLoginBtn')?.classList.remove('hidden');
}
function showAuthenticatedPersonnel(method='SESSION'){
  if(!authenticatedPersonnel)return;
  const box=$('personnelLogin');if(box)box.classList.add('hidden');
  const logout=$('personnelLogoutBtn');if(logout){logout.classList.remove('hidden');logout.disabled=Boolean(active)||personnelAuthLoading}
  $('personnelStatus').className='status ok';
  const via=method==='NFC'?' · NFC':'';
  $('personnelStatus').textContent=`✓ Angemeldet: ${authenticatedPersonnel.personnelNo} - ${authenticatedPersonnel.fullName}${via}${authTransportSuffix()}`;
  notifyWorkerFlow();
}

const baseUpdateBookState=updateBookState;
updateBookState=function(){
  baseUpdateBookState();
  const authOk=personnelSessionMatchesSelection();
  if(!authOk)$('bookBtn').disabled=true;
  if($('personnelLoginBtn'))$('personnelLoginBtn').disabled=Boolean(active)||personnelAuthLoading||!selectedPersonnel;
  if($('personnelLogoutBtn'))$('personnelLogoutBtn').disabled=Boolean(active)||personnelAuthLoading||!authOk;
  if($('personnelPassword'))$('personnelPassword').disabled=Boolean(active)||personnelAuthLoading||!selectedPersonnel||authOk;
  if($('machineScanBtn'))$('machineScanBtn').disabled=Boolean(active)||!authOk;
  if($('addSourceBtn')&&!authOk)$('addSourceBtn').disabled=true;
  notifyWorkerFlow();
};

const baseValues=values;
values=function(){if(!personnelSessionMatchesSelection())throw new Error('Mitarbeiter muss per NFC oder mit Personalnummer und Passwort angemeldet sein.');return baseValues()};

const baseChoosePersonnel=choosePersonnel;
choosePersonnel=function(p){
  authenticatedPersonnel=null;resetPersonnelPassword();baseChoosePersonnel(p);showManualPersonnelLogin();
  $('personnelStatus').className='status neutral';
  $('personnelStatus').textContent=personnelAuthConfigured?`Passwort eingeben und anmelden${personnelAuthHttps?'.':' · HTTP-Testbetrieb.'}`:'⛔ Passwortprüfung im Backend ist nicht konfiguriert.';
  updateBookState();setTimeout(()=>$('personnelPassword')?.focus(),0);
};

const baseClearPersonnelSelection=clearPersonnelSelection;
clearPersonnelSelection=function(message){
  authenticatedPersonnel=null;resetPersonnelPassword();$('personnelLogin')?.classList.add('hidden');$('personnelLogoutBtn')?.classList.add('hidden');
  baseClearPersonnelSelection(message);notifyWorkerFlow();
};

window.acceptAuthenticatedNfcPersonnel=function(person){
  if(!person?.personnelNo||!person?.fullName)return false;
  resetPersonnelPassword();
  baseChoosePersonnel({personnelNo:person.personnelNo,fullName:person.fullName});
  authenticatedPersonnel={personnelNo:person.personnelNo,fullName:person.fullName};
  personnelAuthHttps=location.protocol==='https:';
  applySessionTiming(person,{freshActivity:true});
  showAuthenticatedPersonnel('NFC');updateBookState();renderSummary();return true;
};

async function loginPersonnel(){
  if(!selectedPersonnel||personnelAuthLoading)return;
  const password=$('personnelPassword').value;
  if(!password){$('personnelStatus').className='status bad';$('personnelStatus').textContent='⛔ Passwort eingeben.';notifyWorkerFlow();return}
  personnelAuthLoading=true;updateBookState();$('personnelStatus').className='status neutral';$('personnelStatus').textContent='Passwort wird geprüft …';
  try{
    const r=await api('/api/personnel/login',{method:'POST',body:JSON.stringify({personnelNo:selectedPersonnel.personnelNo,password})});
    resetPersonnelPassword();
    if(!r.ok){authenticatedPersonnel=null;showManualPersonnelLogin();$('personnelStatus').className='status bad';$('personnelStatus').textContent='⛔ '+(r.body?.message||'Anmeldung fehlgeschlagen.');return}
    personnelAuthHttps=r.body?.https!==false;authenticatedPersonnel={personnelNo:r.body.personnelNo,fullName:r.body.fullName};applySessionTiming(r.body,{freshActivity:true});showAuthenticatedPersonnel('PASSWORD');
  }catch{
    authenticatedPersonnel=null;resetPersonnelPassword();showManualPersonnelLogin();$('personnelStatus').className='status bad';$('personnelStatus').textContent='⛔ Anmeldung konnte nicht geprüft werden.';
  }finally{personnelAuthLoading=false;updateBookState();renderSummary();notifyWorkerFlow()}
}

async function logoutPersonnel(reason='manual'){
  if(personnelAuthLoading)return;personnelAuthLoading=true;updateBookState();
  try{await api('/api/personnel/logout',{method:'POST',body:'{}'})}finally{
    authenticatedPersonnel=null;resetPersonnelPassword();clearPersonnelIdleTracking();personnelAuthLoading=false;
    if(selectedPersonnel){
      showManualPersonnelLogin();
      $('personnelStatus').className=reason==='idle'?'status bad':'status neutral';
      $('personnelStatus').textContent=reason==='idle'
        ?'⛔ Wegen Inaktivität automatisch abgemeldet. Bitte erneut per NFC oder mit Passwort anmelden.'
        :'Abgemeldet. NFC verwenden oder Passwort eingeben.';
    }
    updateBookState();renderSummary();notifyWorkerFlow();
  }
}

async function restorePersonnelSession(){
  try{
    const r=await api('/api/personnel/session');if(!r.ok)return;
    personnelAuthConfigured=r.body?.authenticationConfigured!==false;personnelAuthHttps=r.body?.https!==false;setPersonnelIdleTimeout(r.body?.idleTimeoutMinutes);
    if(r.body?.authenticated&&r.body.personnelNo&&r.body.fullName){
      const p={personnelNo:r.body.personnelNo,fullName:r.body.fullName};baseChoosePersonnel(p);authenticatedPersonnel=p;applySessionTiming(r.body);showAuthenticatedPersonnel('SESSION');updateBookState();renderSummary();
    }else clearPersonnelIdleTracking();
  }catch{}finally{notifyWorkerFlow()}
}

const baseSendRequest=sendRequest;
sendRequest=async function(r){
  const session=await api('/api/personnel/session');
  if(!session.ok||!session.body?.authenticated||session.body.personnelNo!==r.personnelNo||session.body.fullName!==r.personnelName){
    expirePersonnelLocally('Anmeldung ist abgelaufen. Bitte erneut per NFC oder mit Passwort anmelden.');return;
  }
  applySessionTiming(session.body);
  return baseSendRequest(r);
};

function initPersonnelAuth(){
  registerPersonnelUserActivity();
  setPersonnelIdleTimeout(window.FamUiConfig?.personnelIdleTimeoutMinutes);
  window.FamUiConfigReady?.then?.(config=>setPersonnelIdleTimeout(config?.personnelIdleTimeoutMinutes)).catch?.(()=>{});
  $('personnelLoginBtn')?.addEventListener('click',()=>loginPersonnel().catch(()=>{}));
  $('personnelLogoutBtn')?.addEventListener('click',()=>logoutPersonnel('manual').catch(()=>{}));
  $('personnelPassword')?.addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();loginPersonnel().catch(()=>{})}});
  restorePersonnelSession().catch(()=>{});
}
window.addEventListener('fam-ui-config-ready',event=>setPersonnelIdleTimeout(event.detail?.personnelIdleTimeoutMinutes));
if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',initPersonnelAuth);else initPersonnelAuth();
