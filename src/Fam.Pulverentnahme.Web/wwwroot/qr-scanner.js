'use strict';

let qrScanState=null;
let qrAudioContext=null;
const QR_FORMATS=['qr_code'];
const QR_ZOOM_STORAGE_KEY='fam-pulver-camera-zoom';

function qrGetAudioContext(){
  if(!qrAudioContext){
    const Ctx=window.AudioContext||window.webkitAudioContext;
    if(Ctx)qrAudioContext=new Ctx();
  }
  return qrAudioContext;
}
async function qrUnlockAudio(){
  try{const ctx=qrGetAudioContext();if(ctx&&ctx.state!=='running')await ctx.resume()}catch{}
}
function qrPlayTone(frequency=880,durationMs=140,gainValue=.16){
  try{
    const ctx=qrGetAudioContext();if(!ctx)return;
    if(ctx.state==='suspended')ctx.resume().catch(()=>{});
    const oscillator=ctx.createOscillator(),gain=ctx.createGain();
    oscillator.type='square';oscillator.frequency.value=frequency;
    gain.gain.setValueAtTime(.0001,ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(gainValue,ctx.currentTime+.01);
    gain.gain.exponentialRampToValueAtTime(.0001,ctx.currentTime+durationMs/1000);
    oscillator.connect(gain);gain.connect(ctx.destination);oscillator.start();oscillator.stop(ctx.currentTime+durationMs/1000+.03);
  }catch{}
}
function qrUi(){return{modal:document.getElementById('qrScannerModal'),title:document.getElementById('qrScannerTitle'),help:document.getElementById('qrScannerHelp'),status:document.getElementById('qrScannerStatus'),video:document.getElementById('qrScannerVideo'),canvas:document.getElementById('qrScannerCanvas'),laser:document.getElementById('qrScanLaser'),close:document.getElementById('qrScannerClose'),scan:document.getElementById('qrScannerScanButton'),zoomIn:document.getElementById('qrZoomIn'),zoomOut:document.getElementById('qrZoomOut')}}
function qrSetStatus(text,kind='neutral'){const e=qrUi().status;if(!e)return;e.className=`status ${kind}`;e.textContent=text}
function qrReadStoredZoom(){
  try{
    const raw=localStorage.getItem(QR_ZOOM_STORAGE_KEY);if(raw===null)return null;
    const value=Number(raw);return Number.isFinite(value)?value:null;
  }catch{return null}
}
function qrStoreZoom(value){try{localStorage.setItem(QR_ZOOM_STORAGE_KEY,String(value))}catch{}}
function qrStopDetection(state=qrScanState){
  if(!state)return;
  if(state.interval){clearInterval(state.interval);state.interval=null}
  state.scanning=false;
  const ui=qrUi();
  ui.laser.classList.add('hidden');
  if(ui.scan){ui.scan.textContent='Scannen';ui.scan.classList.remove('scanning')}
}
function qrCleanup(){
  const state=qrScanState;if(!state)return;
  state.closed=true;qrStopDetection(state);
  if(state.stream)state.stream.getTracks().forEach(t=>t.stop());
  const ui=qrUi();ui.video.srcObject=null;ui.modal.classList.add('hidden');ui.laser.classList.add('hidden');
  if(ui.scan)ui.scan.disabled=true;
  qrScanState=null;
}
function qrCancel(){const state=qrScanState;if(!state)return;const reject=state.reject;qrCleanup();reject(new DOMException('QR-Scan wurde beendet.','AbortError'))}
function qrDrawVisibleArea(){const ui=qrUi(),video=ui.video,canvas=ui.canvas;if(!video.videoWidth||!video.videoHeight)return false;const box=video.parentElement,cw=box.clientWidth,ch=box.clientHeight;if(!cw||!ch)return false;const vw=video.videoWidth,vh=video.videoHeight,containerAspect=cw/ch,videoAspect=vw/vh;let sx=0,sy=0,sw=vw,sh=vh;if(videoAspect>containerAspect){sw=vh*containerAspect;sx=(vw-sw)/2}else{sh=vw/containerAspect;sy=(vh-sh)/2}const targetWidth=Math.min(1280,Math.round(cw*2)),targetHeight=Math.round(targetWidth/containerAspect);canvas.width=targetWidth;canvas.height=targetHeight;const ctx=canvas.getContext('2d',{willReadFrequently:true});if(!ctx)return false;ctx.clearRect(0,0,targetWidth,targetHeight);ctx.drawImage(video,sx,sy,sw,sh,0,0,targetWidth,targetHeight);return true}
async function qrInitZoom(track){
  const ui=qrUi();ui.zoomIn.disabled=true;ui.zoomOut.disabled=true;if(!track||typeof track.getCapabilities!=='function')return;
  try{
    const caps=track.getCapabilities();if(!caps.zoom||typeof caps.zoom.min!=='number'||typeof caps.zoom.max!=='number')return;
    const settings=typeof track.getSettings==='function'?track.getSettings():{};
    const state={min:caps.zoom.min,max:caps.zoom.max,step:typeof caps.zoom.step==='number'&&caps.zoom.step>0?caps.zoom.step:.1,current:typeof settings.zoom==='number'?settings.zoom:caps.zoom.min};
    const clamp=value=>Math.min(state.max,Math.max(state.min,value));
    const update=()=>{ui.zoomOut.disabled=state.current<=state.min+.0001;ui.zoomIn.disabled=state.current>=state.max-.0001};
    const apply=async(value,persist)=>{
      const next=clamp(value);
      await track.applyConstraints({advanced:[{zoom:next}]});
      const after=typeof track.getSettings==='function'?track.getSettings():{};
      state.current=typeof after.zoom==='number'?after.zoom:next;
      if(persist)qrStoreZoom(state.current);
      update();
    };
    const set=async direction=>apply(state.current+direction*state.step,true);
    ui.zoomIn.onclick=()=>set(1).catch(()=>qrSetStatus('Zoom konnte nicht gesetzt werden.','bad'));
    ui.zoomOut.onclick=()=>set(-1).catch(()=>qrSetStatus('Zoom konnte nicht gesetzt werden.','bad'));

    const stored=qrReadStoredZoom();
    if(stored!==null){
      try{await apply(stored,false)}catch{update()}
    }else update();
  }catch{}
}
function qrStartDetection(state){
  if(!state||state.closed||state.scanning)return;
  const ui=qrUi();state.scanning=true;ui.scan.textContent='Scannen stoppen';ui.scan.classList.add('scanning');ui.laser.classList.remove('hidden');qrSetStatus('Scan aktiv. QR-Code jetzt in das Kamerafenster halten.','neutral');
  state.interval=setInterval(async()=>{
    if(state.closed||!state.scanning||state.busy||ui.video.readyState<2)return;
    state.busy=true;
    try{
      if(!qrDrawVisibleArea())return;
      const codes=await state.detector.detect(ui.canvas),found=Array.isArray(codes)?codes.find(c=>String(c.rawValue||'').trim()):null;
      if(!found)return;
      const raw=String(found.rawValue).trim(),now=Date.now();
      if(raw===state.last&&now-state.lastAt<1200)return;
      state.last=raw;state.lastAt=now;
      if(navigator.vibrate)navigator.vibrate(50);qrPlayTone();
      qrStopDetection(state);
      if(typeof state.onDetected==='function'){
        try{await state.onDetected(raw)}catch(error){console.error('QR preview callback error',error)}
      }
      if(state.closed)return;
      const done=state.resolve;qrCleanup();done(raw);
    }catch(error){console.error('QR detect error',error)}finally{if(qrScanState)state.busy=false}
  },220);
}
function qrToggleDetection(){
  const state=qrScanState;if(!state||state.closed)return;
  qrUnlockAudio().catch(()=>{});
  if(state.scanning){qrStopDetection(state);qrSetStatus('Scan pausiert. Zoom kann eingestellt werden. Zum Fortsetzen „Scannen“ drücken.','neutral')}
  else qrStartDetection(state);
}
async function scanQrCode({title='QR-Code scannen',help='QR-Code in das Kamerafenster halten.',onDetected=null}={}){
  if(qrScanState)throw new Error('Es läuft bereits ein QR-Scan.');
  await qrUnlockAudio();
  if(!window.isSecureContext)throw new Error('QR-Scan benötigt HTTPS bzw. einen sicheren Browserkontext.');
  if(!navigator.mediaDevices?.getUserMedia)throw new Error('Kamera-Zugriff wird von diesem Browser nicht unterstützt.');
  if(!('BarcodeDetector' in window))throw new Error('Dieser Browser unterstützt die native QR-Erkennung nicht.');
  if(typeof BarcodeDetector.getSupportedFormats==='function'){const supported=await BarcodeDetector.getSupportedFormats();if(!supported.includes('qr_code'))throw new Error('Dieser Browser unterstützt QR-Code-Erkennung nicht.');}
  const detector=new BarcodeDetector({formats:QR_FORMATS}),ui=qrUi();ui.title.textContent=title;ui.help.textContent=help;ui.modal.classList.remove('hidden');ui.laser.classList.add('hidden');ui.scan.disabled=true;ui.scan.textContent='Scannen';ui.scan.classList.remove('scanning');qrSetStatus('Kamera wird gestartet …');
  return await new Promise(async(resolve,reject)=>{
    const state={resolve,reject,detector,onDetected,stream:null,track:null,interval:null,busy:false,closed:false,scanning:false,last:'',lastAt:0};qrScanState=state;ui.close.onclick=qrCancel;ui.scan.onclick=qrToggleDetection;
    try{
      state.stream=await navigator.mediaDevices.getUserMedia({audio:false,video:{facingMode:{ideal:'environment'},width:{ideal:1280},height:{ideal:720}}});
      state.track=state.stream.getVideoTracks()[0]||null;ui.video.srcObject=state.stream;await ui.video.play();await qrInitZoom(state.track);ui.scan.disabled=false;qrSetStatus('Kamera bereit. Bei Bedarf Zoom einstellen und anschließend „Scannen“ drücken.','neutral');
    }catch(error){qrCleanup();reject(error)}
  });
}
window.scanQrCode=scanQrCode;
