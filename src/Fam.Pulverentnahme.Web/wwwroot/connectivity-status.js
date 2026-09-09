'use strict';

// Operator-visible connectivity status. This is only a reachability indicator; it never turns a
// cached/offline state into a booking authorization. Productive operations remain guarded by the
// backend's current Oxaion validation and transaction rules.
(function(){
  const CHECK_INTERVAL_MS=60000;
  const BACKEND_TIMEOUT_MS=4000;
  const OXAION_TIMEOUT_MS=6000;
  let checking=false;
  let initialized=false;
  let lastState='checking';
  let retryTimer=null;

  const el=id=>document.getElementById(id);

  async function fetchWithTimeout(url,timeoutMs){
    const controller=new AbortController();
    const timeout=setTimeout(()=>controller.abort(),timeoutMs);
    try{
      return await fetch(url,{cache:'no-store',credentials:'same-origin',headers:{Accept:'application/json'},signal:controller.signal});
    }finally{
      clearTimeout(timeout);
    }
  }

  function stamp(){
    try{return new Intl.DateTimeFormat('de-AT',{hour:'2-digit',minute:'2-digit',second:'2-digit'}).format(new Date())}
    catch{return new Date().toLocaleTimeString()}
  }

  function setLamp(state,label,title){
    const lamp=el('connectivityLamp');
    if(!lamp)return;
    lamp.className=`connectivityLamp ${state}`;
    const text=el('connectivityLampText');if(text)text.textContent=label;
    lamp.title=title;
    lamp.setAttribute('aria-label',title);
    lamp.dataset.state=state;
    lastState=state;
  }

  function setBanner(state,text,{force=false,autoHide=false}={}){
    const banner=el('startupConnectivity');
    const message=el('startupConnectivityText');
    if(!banner||!message)return;
    if(force||!initialized||state==='bad'||lastState==='bad')banner.classList.remove('hidden');
    banner.classList.remove('checking','ok','bad');
    banner.classList.add(state);
    message.textContent=text;
    if(autoHide){
      const current=text;
      setTimeout(()=>{
        if(message.textContent===current&&banner.classList.contains('ok'))banner.classList.add('hidden');
      },2200);
    }
  }

  function setChecking(){
    setLamp('checking','Prüfen','Verbindung zu Backend und Oxaion wird geprüft.');
    if(!initialized)setBanner('checking','Verbindungsaufbau: Backend wird geprüft …',{force:true});
  }

  function scheduleFailureRetry(){
    clearTimeout(retryTimer);
    retryTimer=setTimeout(()=>checkConnectivity('retry'),15000);
  }

  async function checkConnectivity(reason='interval'){
    if(checking||document.visibilityState==='hidden')return;
    checking=true;
    const wasBad=lastState==='bad';
    setChecking();
    try{
      let backend;
      try{backend=await fetchWithTimeout('/api/health',BACKEND_TIMEOUT_MS)}catch{backend=null}
      if(!backend?.ok){
        const text=navigator.onLine===false
          ?'Keine Netzwerkverbindung. Die App ist lokal verfügbar, Oxaion ist derzeit nicht erreichbar.'
          :'Keine Verbindung zum Backend. Die App ist lokal verfügbar, Oxaion kann derzeit nicht geprüft werden.';
        setLamp('bad','Offline',`${text} Letzte Prüfung: ${stamp()}`);
        setBanner('bad',text,{force:true});
        scheduleFailureRetry();
        return;
      }

      if(!initialized)setBanner('checking','Backend erreichbar. Verbindung zu Oxaion wird geprüft …',{force:true});

      // The periodic operator lamp uses the lightweight tunnel-connect probe. The much heavier
      // /api/health/oxaion dialog smoke test remains a Dev-Info/manual diagnostic and must not
      // delay normal app startup or foreground checks.
      let oxaion;
      try{oxaion=await fetchWithTimeout('/api/connectivity/oxaion',OXAION_TIMEOUT_MS)}catch{oxaion=null}
      if(!oxaion?.ok){
        const text='Backend erreichbar, aber aktuell keine Verbindung zu Oxaion.';
        setLamp('bad','Oxaion offline','Oxaion nicht erreichbar. '+`Letzte Prüfung: ${stamp()}`);
        setBanner('bad',text,{force:true});
        scheduleFailureRetry();
        return;
      }

      clearTimeout(retryTimer);
      setLamp('ok','Oxaion','Oxaion erreichbar. '+`Letzte Prüfung: ${stamp()}`);
      const text=wasBad?'Verbindung zu Oxaion wiederhergestellt.':'Oxaion-Verbindung aktiv.';
      setBanner('ok',text,{force:!initialized||wasBad,autoHide:true});
    }finally{
      initialized=true;
      checking=false;
    }
  }

  function install(){
    setChecking();
    checkConnectivity('startup');
    setInterval(()=>checkConnectivity('interval'),CHECK_INTERVAL_MS);
    window.addEventListener('online',()=>checkConnectivity('online'));
    window.addEventListener('offline',()=>{
      clearTimeout(retryTimer);
      setLamp('bad','Offline','Keine Netzwerkverbindung.');
      setBanner('bad','Keine Netzwerkverbindung. Die App ist lokal verfügbar, Oxaion ist derzeit nicht erreichbar.',{force:true});
    });
    document.addEventListener('visibilitychange',()=>{if(document.visibilityState==='visible')checkConnectivity('foreground')});
  }

  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',install,{once:true});else install();
})();
