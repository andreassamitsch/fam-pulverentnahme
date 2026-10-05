'use strict';

let nfcScanning=false;
let nfcAudioContext=null;

function setNfcStatus(text,kind='neutral'){
  const e=document.getElementById('nfcStatus');
  if(!e)return;
  e.className=`status ${kind}`;
  e.textContent=text;
}
function getNfcAudioContext(){
  if(!nfcAudioContext){
    const Ctx=window.AudioContext||window.webkitAudioContext;
    if(Ctx)nfcAudioContext=new Ctx();
  }
  return nfcAudioContext;
}
async function unlockNfcAudio(){
  try{const ctx=getNfcAudioContext();if(ctx&&ctx.state!=='running')await ctx.resume()}catch{}
}
function playNfcDetectedSignal(){
  try{
    const ctx=getNfcAudioContext();if(!ctx)return;
    if(ctx.state==='suspended')ctx.resume().catch(()=>{});
    const oscillator=ctx.createOscillator(),gain=ctx.createGain();
    oscillator.type='square';oscillator.frequency.value=880;
    gain.gain.setValueAtTime(.0001,ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(.18,ctx.currentTime+.01);
    gain.gain.exponentialRampToValueAtTime(.0001,ctx.currentTime+.14);
    oscillator.connect(gain);gain.connect(ctx.destination);oscillator.start();oscillator.stop(ctx.currentTime+.17);
    if(navigator.vibrate)navigator.vibrate(60);
  }catch(error){console.error('NFC audio error',error)}
}
function nfcErrorMessage(error){
  if(error?.name==='NotAllowedError')return 'NFC-Berechtigung wurde nicht erteilt. Bitte NFC erlauben und erneut versuchen.';
  if(error?.name==='NotSupportedError')return 'Dieser NFC-Chip bzw. dieses Gerät wird von Web NFC nicht unterstützt.';
  if(error?.name==='NotReadableError')return 'NFC konnte nicht gelesen werden. Bitte NFC am Smartphone aktivieren und erneut versuchen.';
  if(error?.name==='AbortError')return 'NFC-Lesen wurde beendet.';
  return error?.message||'NFC-Lesen ist fehlgeschlagen.';
}
async function readOneNfcTag(){
  const reader=new NDEFReader();
  const controller=new AbortController();
  return await new Promise(async(resolve,reject)=>{
    let done=false,timer=null;
    const finish=(fn,value)=>{if(done)return;done=true;if(timer)clearTimeout(timer);try{controller.abort()}catch{}fn(value)};
    reader.addEventListener('reading',event=>{playNfcDetectedSignal();finish(resolve,event)},{once:true});
    reader.addEventListener('readingerror',()=>finish(reject,new Error('Chip erkannt, aber nicht als NDEF-kompatibler NFC-Tag lesbar.')),{once:true});
    timer=setTimeout(()=>finish(reject,new Error('Kein NFC-Chip erkannt. Bitte erneut starten und den Chip an die NFC-Antenne des Smartphones halten.')),30000);
    try{await reader.scan({signal:controller.signal});setNfcStatus('NFC-Leser aktiv. Chip jetzt an das Smartphone halten …','neutral')}catch(error){finish(reject,error)}
  });
}
async function startNfcPersonnelScan(){
  if(nfcScanning)return;
  if(typeof active!=='undefined'&&active){setNfcStatus('Während eines offenen Buchungsvorgangs kann der Mitarbeiter nicht gewechselt werden.','bad');return}
  if(!window.isSecureContext){setNfcStatus('NFC benötigt HTTPS. Bitte notfalls unten mit Personalnummer und Passwort anmelden.','bad');return}
  if(!('NDEFReader' in window)){setNfcStatus('Dieser Browser unterstützt Web NFC nicht. Bitte unten mit Personalnummer und Passwort anmelden.','bad');return}
  await unlockNfcAudio();
  const button=document.getElementById('nfcScanBtn');nfcScanning=true;button.disabled=true;setNfcStatus('NFC wird gestartet …','neutral');
  try{
    const event=await readOneNfcTag(),serialNumber=String(event.serialNumber||'').trim();
    if(!serialNumber)throw new Error('Der NFC-Chip liefert keine Seriennummer/UID. Eine sichere RFID-Zuordnung ist nicht möglich.');
    setNfcStatus('Chip erkannt. Mitarbeiter wird geprüft …','neutral');
    const response=await api('/api/personnel/nfc',{method:'POST',body:JSON.stringify({serialNumber})});
    if(!response.ok)throw new Error(response.body?.message||response.body?.detail||response.body?.error||'RFID-Zuordnung konnte nicht gelesen werden.');
    const person=response.body;if(!person?.personnelNo||!person?.fullName)throw new Error('Backend hat keine eindeutige Mitarbeiterzuordnung geliefert.');
    if(typeof window.acceptAuthenticatedNfcPersonnel==='function')window.acceptAuthenticatedNfcPersonnel(person);
    else choosePersonnel({personnelNo:person.personnelNo,fullName:person.fullName});
    setNfcStatus(`✓ Angemeldet: ${person.personnelNo} - ${person.fullName}`,'ok');
  }catch(error){setNfcStatus('⛔ '+nfcErrorMessage(error),'bad')}finally{
    nfcScanning=false;button.disabled=Boolean(typeof active!=='undefined'&&active);
    if(typeof window.refreshWorkerFlow==='function')window.refreshWorkerFlow();
  }
}
function initNfcPersonnel(){
  const button=document.getElementById('nfcScanBtn');if(!button)return;
  button.addEventListener('click',()=>startNfcPersonnelScan().catch(error=>setNfcStatus('⛔ '+nfcErrorMessage(error),'bad')));
  if(!window.isSecureContext)setNfcStatus('NFC benötigt HTTPS. Fallback: Personalnummer + Passwort.','neutral');
  else if(!('NDEFReader' in window))setNfcStatus('Web NFC nicht verfügbar. Fallback: Personalnummer + Passwort.','neutral');
  else setNfcStatus('NFC bereit. Chip zur Anmeldung lesen.','neutral');
}
if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',initNfcPersonnel);else initNfcPersonnel();
