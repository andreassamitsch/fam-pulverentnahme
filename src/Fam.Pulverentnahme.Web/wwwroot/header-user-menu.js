'use strict';

// Compact authenticated-user presentation in the sticky app header.
// Authentication itself remains owned by personnel-auth.js and the backend session.
(function(){
  let installed=false;
  let menuOpen=false;

  const el=id=>document.getElementById(id);
  const auth=()=>typeof personnelSessionMatchesSelection==='function'&&personnelSessionMatchesSelection();
  const person=()=>{
    if(!auth())return null;
    if(typeof selectedPersonnel!=='undefined'&&selectedPersonnel?.fullName)return selectedPersonnel;
    if(typeof authenticatedPersonnel!=='undefined'&&authenticatedPersonnel?.fullName)return authenticatedPersonnel;
    return null;
  };

  function closeMenu(){
    menuOpen=false;
    el('headerPersonnelMenu')?.classList.add('hidden');
    const trigger=el('headerPersonnelName');
    if(trigger)trigger.setAttribute('aria-expanded','false');
  }

  function openMenu(){
    if(!auth())return;
    menuOpen=true;
    const menu=el('headerPersonnelMenu');
    if(menu)menu.classList.remove('hidden');
    const trigger=el('headerPersonnelName');
    if(trigger)trigger.setAttribute('aria-expanded','true');
    setTimeout(()=>el('headerPersonnelLogout')?.focus(),20);
  }

  function toggleMenu(){menuOpen?closeMenu():openMenu()}

  function ensureMenu(){
    const trigger=el('headerPersonnelName');
    if(!trigger)return false;

    trigger.classList.add('shellPersonnelInteractive');
    trigger.setAttribute('role','button');
    trigger.setAttribute('tabindex','0');
    trigger.setAttribute('aria-haspopup','dialog');
    trigger.setAttribute('aria-expanded','false');
    trigger.setAttribute('title','Angemeldeten Mitarbeiter anzeigen');

    if(!el('headerPersonnelMenu')){
      const menu=document.createElement('div');
      menu.id='headerPersonnelMenu';
      menu.className='headerPersonnelMenu hidden';
      menu.setAttribute('role','dialog');
      menu.setAttribute('aria-label','Angemeldeter Mitarbeiter');
      menu.innerHTML=`
        <div class="headerPersonnelIdentity">
          <div class="headerPersonnelAvatar" aria-hidden="true">👤</div>
          <div>
            <span>Angemeldet</span>
            <b id="headerPersonnelMenuName"></b>
          </div>
        </div>
        <button id="headerPersonnelLogout" class="secondary" type="button">Abmelden</button>`;
      document.body.appendChild(menu);
    }

    if(!trigger.dataset.userMenuBound){
      trigger.dataset.userMenuBound='true';
      trigger.addEventListener('click',event=>{event.stopPropagation();toggleMenu()});
      trigger.addEventListener('keydown',event=>{
        if(event.key==='Enter'||event.key===' '){event.preventDefault();toggleMenu()}
      });
    }

    const logout=el('headerPersonnelLogout');
    if(logout&&!logout.dataset.userMenuBound){
      logout.dataset.userMenuBound='true';
      logout.addEventListener('click',async()=>{
        if(typeof active!=='undefined'&&active){
          alert('Dieser Vorgang ist noch offen. Zuerst Buchungsstatus klären.');
          return;
        }
        const hasPreparedInput=document.body.classList.contains('processShellProcess')||Boolean(document.querySelector('.processChoice.active'));
        if(hasPreparedInput&&!confirm('Abmelden? Nicht gebuchte Eingaben des aktuellen Vorgangs werden verworfen.'))return;
        if(typeof logoutPersonnel!=='function')return;
        logout.disabled=true;
        try{
          await logoutPersonnel();
          closeMenu();
          // Reload after the server-side logout so no unsent process input of the previous operator
          // can remain hidden in the DOM for the next login. Open/recovery transactions are blocked above.
          location.reload();
        }catch{
          logout.disabled=false;
          sync();
        }
      });
    }
    return true;
  }

  function sync(){
    if(!ensureMenu())return;
    const ok=auth();
    const current=person();
    const fullName=current?.fullName||'';
    document.body.classList.toggle('shellUserAuthenticated',ok);

    const trigger=el('headerPersonnelName');
    if(trigger){
      // process-shell.js writes the full name too; keep the header trigger authoritative here.
      if(ok&&fullName)trigger.textContent=fullName;
      trigger.classList.toggle('hidden',!ok||!fullName);
      trigger.setAttribute('aria-label',ok&&fullName?`${fullName}. Menü öffnen.`:'Angemeldeten Mitarbeiter anzeigen');
    }
    const name=el('headerPersonnelMenuName');if(name)name.textContent=fullName;
    const logout=el('headerPersonnelLogout');
    if(logout)logout.disabled=!ok||Boolean(typeof active!=='undefined'&&active)||Boolean(typeof personnelAuthLoading!=='undefined'&&personnelAuthLoading);
    if(!ok)closeMenu();
  }

  function install(){
    if(installed)return;installed=true;
    const style=document.createElement('style');
    style.id='headerPersonnelMenuStyles';
    style.textContent=`
.shellPersonnelInteractive{display:inline-flex;align-items:center;gap:6px;width:max-content;max-width:56vw;margin-top:4px;padding:4px 7px;border-radius:8px;cursor:pointer;color:#e6f4ff;background:rgba(255,255,255,.08);outline:none}.shellPersonnelInteractive:before{content:'👤';font-size:14px;line-height:1}.shellPersonnelInteractive:after{content:'▾';font-size:10px;opacity:.8}.shellPersonnelInteractive:hover,.shellPersonnelInteractive:focus{background:rgba(255,255,255,.16);box-shadow:0 0 0 2px rgba(255,255,255,.18)}.headerPersonnelMenu{position:fixed;z-index:1200;top:68px;right:12px;width:min(330px,calc(100vw - 24px));padding:14px;background:#fff;color:#17212b;border:1px solid #cbd8e0;border-radius:14px;box-shadow:0 16px 45px rgba(0,0,0,.28)}.headerPersonnelIdentity{display:flex;align-items:center;gap:12px;margin-bottom:12px}.headerPersonnelAvatar{display:grid;place-items:center;flex:0 0 46px;width:46px;height:46px;border-radius:50%;background:#e8f3fb;font-size:25px}.headerPersonnelIdentity span{display:block;font-size:11px;font-weight:800;text-transform:uppercase;letter-spacing:.05em;color:#71808b}.headerPersonnelIdentity b{display:block;margin-top:2px;font-size:17px;line-height:1.2;overflow-wrap:anywhere}.headerPersonnelMenu button{width:100%;min-height:46px}.processShellHome.shellUserAuthenticated #loginStep{display:none!important}@media(max-width:680px){.shellPersonnelInteractive{max-width:48vw;font-size:12px;padding:3px 6px}.headerPersonnelMenu{top:64px;right:8px;width:calc(100vw - 16px)}}
`;
    document.head.appendChild(style);

    document.addEventListener('click',event=>{
      if(!menuOpen)return;
      if(event.target?.closest?.('#headerPersonnelMenu')||event.target?.closest?.('#headerPersonnelName'))return;
      closeMenu();
    });
    document.addEventListener('keydown',event=>{if(event.key==='Escape')closeMenu()});
    window.addEventListener('resize',closeMenu);

    sync();
    setInterval(sync,400);
  }

  function start(){
    let attempts=0;
    const timer=setInterval(()=>{
      attempts++;
      if(el('headerPersonnelName')){clearInterval(timer);install()}
      else if(attempts>150)clearInterval(timer);
    },30);
  }

  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',start);else start();
})();
