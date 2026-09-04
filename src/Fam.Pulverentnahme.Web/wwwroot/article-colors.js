'use strict';

function recognitionHex(value){
  const hex=String(value||'').trim().replace(/^#/,'').toUpperCase();
  return /^[0-9A-F]{6}$/.test(hex)?hex:null;
}
function renderArticleRecognitionColors(){
  const panel=document.getElementById('articleRecognitionPanel');
  const left=document.getElementById('articleRecognitionColor1');
  const right=document.getElementById('articleRecognitionColor2');
  const text=document.getElementById('articleRecognitionText');
  const status=document.getElementById('articleRecognitionStatus');
  if(!panel||!left||!right||!text||!status)return;

  const result=(typeof machineStock!=='undefined'&&machineStock)?machineStock.recognitionColors:null;
  const article=document.getElementById('articleInfo')?.textContent?.trim()||'';
  if(!article){panel.classList.add('hidden');return}

  panel.classList.remove('hidden');
  const c1=result?.color1,c2=result?.color2;
  const h1=recognitionHex(c1?.hex),h2=recognitionHex(c2?.hex);
  left.style.background=h1?`#${h1}`:'transparent';
  right.style.background=h2?`#${h2}`:'transparent';
  left.classList.toggle('recognitionColorMissing',!h1);
  right.classList.toggle('recognitionColorMissing',!h2);

  const label1=h1?`EFA01 ${c1?.name||''} (#${h1})`:'EFA01 nicht verfügbar';
  const label2=h2?`EFA02 ${c2?.name||''} (#${h2})`:'EFA02 nicht verfügbar';
  text.textContent=`${label1} · ${label2}`;

  const complete=result?.status==='COMPLETE'&&h1&&h2;
  status.className=`recognitionColorStatus ${complete?'ok':'warn'}`;
  status.textContent=complete?'Erkennungsfarben aus den Oxaion-Sachmerkmalen.':(result?.message||'Erkennungsfarben EFA01/EFA02 sind nicht verfügbar.');
  panel.setAttribute('aria-label',`${article}: ${label1}; ${label2}`);
}
function initArticleRecognitionColors(){
  const article=document.getElementById('articleInfo');
  const machineInfo=document.getElementById('machineInfo');
  if(article)new MutationObserver(renderArticleRecognitionColors).observe(article,{childList:true,subtree:true,characterData:true});
  if(machineInfo)new MutationObserver(()=>{if(!machineInfo.classList.contains('hidden'))renderArticleRecognitionColors()}).observe(machineInfo,{attributes:true,attributeFilter:['class']});
  renderArticleRecognitionColors();
}
if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',initArticleRecognitionColors);else initArticleRecognitionColors();

// Bootstrap the separate-process worker extension. Kept as a separate file so the accepted
// replenishment flow stays unchanged while the new processes are developed and tested.
(function loadSeparateProcesses(){
  const s=document.createElement('script');
  s.src='/process-mode.js?v=20260904-separate-processes-1';
  s.defer=true;
  document.head.appendChild(s);
})();
