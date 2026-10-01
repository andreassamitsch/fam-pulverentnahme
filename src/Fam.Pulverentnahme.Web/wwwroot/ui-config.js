'use strict';

window.FamUiConfig={developerToolsEnabled:false};
window.FamUiConfigReady=(async()=>{
  try{
    const response=await fetch('/api/ui-config',{cache:'no-store',credentials:'same-origin'});
    if(response.ok){
      const data=await response.json();
      window.FamUiConfig={developerToolsEnabled:data?.developerToolsEnabled===true};
    }
  }catch{}
  document.documentElement.classList.toggle('developer-tools-enabled',window.FamUiConfig.developerToolsEnabled===true);
  window.dispatchEvent(new CustomEvent('fam-ui-config-ready',{detail:window.FamUiConfig}));
  return window.FamUiConfig;
})();
