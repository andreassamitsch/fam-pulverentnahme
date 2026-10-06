'use strict';

window.FamUiConfig={developerToolsEnabled:false,environment:'STAGING',personnelIdleTimeoutMinutes:480};
window.FamUiConfigReady=(async()=>{
  try{
    const response=await fetch('/api/ui-config',{cache:'no-store',credentials:'same-origin'});
    if(response.ok){
      const data=await response.json();
      window.FamUiConfig={
        developerToolsEnabled:data?.developerToolsEnabled===true,
        environment:String(data?.environment||'STAGING').toUpperCase(),
        personnelIdleTimeoutMinutes:Number(data?.personnelIdleTimeoutMinutes)||480==='PRODUCTION'?'PRODUCTION':'STAGING'
      };
    }
  }catch{}
  document.documentElement.classList.toggle('developer-tools-enabled',window.FamUiConfig.developerToolsEnabled===true);
  const env=window.FamUiConfig.environment||'STAGING';
  const badge=document.getElementById('environmentBadge');
  if(badge){badge.textContent=env==='PRODUCTION'?'PROD':'STG';badge.classList.toggle('production',env==='PRODUCTION');badge.classList.toggle('staging',env!=='PRODUCTION')}
  const detail=document.getElementById('environmentDetail');
  if(detail)detail.textContent='Frontend + ASP.NET Core Backend · '+env;
  const notice=document.getElementById('environmentNotice');
  if(notice)notice.innerHTML=env==='PRODUCTION'?'<b>PRODUCTION:</b> Produktivumgebung – Buchungen wirken in Oxaion Produktion.':'<b>STAGING:</b> Testumgebung – Buchungen wirken in Oxaion STAGING.';
  window.dispatchEvent(new CustomEvent('fam-ui-config-ready',{detail:window.FamUiConfig}));
  return window.FamUiConfig;
})();
