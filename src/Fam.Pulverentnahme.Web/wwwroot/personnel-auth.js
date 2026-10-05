'use strict';

let authenticatedPersonnel=null,personnelAuthLoading=false,personnelAuthConfigured=true,personnelAuthHttps=location.protocol==='https:';

function personnelSessionMatchesSelection(){return Boolean(authenticatedPersonnel&&selectedPersonnel&&authenticatedPersonnel.personnelNo===selectedPersonnel.personnelNo&&authenticatedPersonnel.fullName===selectedPersonnel.fullName)}
function resetPersonnelPassword(){const e=$('personnelPassword');if(e)e.value=''}
function authTransportSuffix(){return personnelAuthHttps?'':' · HTTP-Testbetrieb'}
function notifyWorkerFlow(){if(typeof window.refreshWorkerFlow==='function')window.refreshWorkerFlow()}
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
    personnelAuthHttps=r.body?.https!==false;authenticatedPersonnel={personnelNo:r.body.personnelNo,fullName:r.body.fullName};showAuthenticatedPersonnel('PASSWORD');
  }catch{
    authenticatedPersonnel=null;resetPersonnelPassword();showManualPersonnelLogin();$('personnelStatus').className='status bad';$('personnelStatus').textContent='⛔ Anmeldung konnte nicht geprüft werden.';
  }finally{personnelAuthLoading=false;updateBookState();renderSummary();notifyWorkerFlow()}
}

async function logoutPersonnel(){
  if(personnelAuthLoading)return;personnelAuthLoading=true;updateBookState();
  try{await api('/api/personnel/logout',{method:'POST',body:'{}'})}finally{
    authenticatedPersonnel=null;resetPersonnelPassword();personnelAuthLoading=false;
    if(selectedPersonnel){showManualPersonnelLogin();$('personnelStatus').className='status neutral';$('personnelStatus').textContent='Abgemeldet. NFC verwenden oder Passwort eingeben.'}
    updateBookState();renderSummary();notifyWorkerFlow();
  }
}

async function restorePersonnelSession(){
  try{
    const r=await api('/api/personnel/session');if(!r.ok)return;
    personnelAuthConfigured=r.body?.authenticationConfigured!==false;personnelAuthHttps=r.body?.https!==false;
    if(r.body?.authenticated&&r.body.personnelNo&&r.body.fullName){
      const p={personnelNo:r.body.personnelNo,fullName:r.body.fullName};baseChoosePersonnel(p);authenticatedPersonnel=p;showAuthenticatedPersonnel('SESSION');updateBookState();renderSummary();
    }
  }catch{}finally{notifyWorkerFlow()}
}

const baseSendRequest=sendRequest;
sendRequest=async function(r){
  const session=await api('/api/personnel/session');
  if(!session.ok||!session.body?.authenticated||session.body.personnelNo!==r.personnelNo||session.body.fullName!==r.personnelName){
    authenticatedPersonnel=null;showManualPersonnelLogin();$('personnelStatus').className='status bad';$('personnelStatus').textContent='⛔ Anmeldung ist abgelaufen. Bitte erneut per NFC oder mit Passwort anmelden.';updateBookState();notifyWorkerFlow();return;
  }
  return baseSendRequest(r);
};

function initPersonnelAuth(){
  $('personnelLoginBtn')?.addEventListener('click',()=>loginPersonnel().catch(()=>{}));
  $('personnelLogoutBtn')?.addEventListener('click',()=>logoutPersonnel().catch(()=>{}));
  $('personnelPassword')?.addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();loginPersonnel().catch(()=>{})}});
  restorePersonnelSession().catch(()=>{});
}
if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',initPersonnelAuth);else initPersonnelAuth();
