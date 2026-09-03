'use strict';

let authenticatedPersonnel=null,personnelAuthLoading=false,personnelAuthConfigured=true,personnelAuthHttps=location.protocol==='https:';

function personnelSessionMatchesSelection(){return Boolean(authenticatedPersonnel&&selectedPersonnel&&authenticatedPersonnel.personnelNo===selectedPersonnel.personnelNo&&authenticatedPersonnel.fullName===selectedPersonnel.fullName)}
function resetPersonnelPassword(){const e=$('personnelPassword');if(e)e.value=''}
function showPersonnelLogin(){const box=$('personnelLogin');if(box)box.classList.toggle('hidden',!selectedPersonnel);$('personnelLogoutBtn')?.classList.add('hidden');$('personnelLoginBtn')?.classList.remove('hidden')}
function authTransportSuffix(){return personnelAuthHttps?'':' · HTTP-Testbetrieb'}
function showAuthenticatedPersonnel(){if(!authenticatedPersonnel)return;const box=$('personnelLogin');if(box)box.classList.remove('hidden');$('personnelLoginBtn')?.classList.add('hidden');$('personnelLogoutBtn')?.classList.remove('hidden');$('personnelStatus').className='status ok';$('personnelStatus').textContent=`✓ Angemeldet: ${authenticatedPersonnel.personnelNo} - ${authenticatedPersonnel.fullName}${authTransportSuffix()}`}

const baseUpdateBookState=updateBookState;
updateBookState=function(){baseUpdateBookState();const authOk=personnelSessionMatchesSelection();if(!authOk)$('bookBtn').disabled=true;if($('personnelLoginBtn'))$('personnelLoginBtn').disabled=Boolean(active)||personnelAuthLoading||!selectedPersonnel;if($('personnelLogoutBtn'))$('personnelLogoutBtn').disabled=Boolean(active)||personnelAuthLoading;if($('personnelPassword'))$('personnelPassword').disabled=Boolean(active)||personnelAuthLoading||!selectedPersonnel||authOk};

const baseValues=values;
values=function(){if(!personnelSessionMatchesSelection())throw new Error('Mitarbeiter muss mit Personalnummer und Passwort angemeldet sein.');return baseValues()};

const baseChoosePersonnel=choosePersonnel;
choosePersonnel=function(p){authenticatedPersonnel=null;resetPersonnelPassword();baseChoosePersonnel(p);showPersonnelLogin();$('personnelStatus').className='status neutral';$('personnelStatus').textContent=personnelAuthConfigured?`Passwort eingeben und Mitarbeiter anmelden${personnelAuthHttps?'.':' · HTTP-Testbetrieb.'}`:'⛔ Passwortprüfung im Backend ist noch nicht konfiguriert.';updateBookState();setTimeout(()=>$('personnelPassword')?.focus(),0)};

const baseClearPersonnelSelection=clearPersonnelSelection;
clearPersonnelSelection=function(message){authenticatedPersonnel=null;resetPersonnelPassword();$('personnelLogin')?.classList.add('hidden');baseClearPersonnelSelection(message)};

async function loginPersonnel(){if(!selectedPersonnel||personnelAuthLoading)return;const password=$('personnelPassword').value;if(!password){$('personnelStatus').className='status bad';$('personnelStatus').textContent='⛔ Passwort eingeben.';return}personnelAuthLoading=true;updateBookState();$('personnelStatus').className='status neutral';$('personnelStatus').textContent='Passwort wird geprüft …';try{const r=await api('/api/personnel/login',{method:'POST',body:JSON.stringify({personnelNo:selectedPersonnel.personnelNo,password})});resetPersonnelPassword();if(!r.ok){authenticatedPersonnel=null;showPersonnelLogin();$('personnelStatus').className='status bad';$('personnelStatus').textContent='⛔ '+(r.body?.message||'Anmeldung fehlgeschlagen.');return}personnelAuthHttps=r.body?.https!==false;authenticatedPersonnel={personnelNo:r.body.personnelNo,fullName:r.body.fullName};showAuthenticatedPersonnel()}catch{authenticatedPersonnel=null;resetPersonnelPassword();showPersonnelLogin();$('personnelStatus').className='status bad';$('personnelStatus').textContent='⛔ Anmeldung konnte nicht geprüft werden.'}finally{personnelAuthLoading=false;updateBookState();renderSummary()}}

async function logoutPersonnel(){if(personnelAuthLoading)return;personnelAuthLoading=true;updateBookState();try{await api('/api/personnel/logout',{method:'POST',body:'{}'})}finally{authenticatedPersonnel=null;resetPersonnelPassword();personnelAuthLoading=false;if(selectedPersonnel){showPersonnelLogin();$('personnelStatus').className='status neutral';$('personnelStatus').textContent=`Passwort eingeben und Mitarbeiter anmelden${personnelAuthHttps?'.':' · HTTP-Testbetrieb.'}`}updateBookState();renderSummary()}}

async function restorePersonnelSession(){try{const r=await api('/api/personnel/session');if(!r.ok)return;personnelAuthConfigured=r.body?.authenticationConfigured!==false;personnelAuthHttps=r.body?.https!==false;if(r.body?.authenticated&&r.body.personnelNo&&r.body.fullName){const p={personnelNo:r.body.personnelNo,fullName:r.body.fullName};baseChoosePersonnel(p);authenticatedPersonnel=p;showAuthenticatedPersonnel();updateBookState();renderSummary()}}catch{}}

const baseSendRequest=sendRequest;
sendRequest=async function(r){const session=await api('/api/personnel/session');if(!session.ok||!session.body?.authenticated||session.body.personnelNo!==r.personnelNo||session.body.fullName!==r.personnelName){authenticatedPersonnel=null;showPersonnelLogin();$('personnelStatus').className='status bad';$('personnelStatus').textContent='⛔ Anmeldung ist abgelaufen. Bitte Passwort erneut eingeben.';updateBookState();return}return baseSendRequest(r)};

$('personnelLoginBtn').onclick=loginPersonnel;
$('personnelLogoutBtn').onclick=logoutPersonnel;
$('personnelPassword').addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();loginPersonnel().catch(()=>{})}});
restorePersonnelSession().catch(()=>{});
