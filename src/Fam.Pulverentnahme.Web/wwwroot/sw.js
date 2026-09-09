const CACHE='fam-pulver-staging-v27-connectivity-pcl-bins-auth-grace-20260909';
const ASSETS=[
  '/',
  '/index.html',
  '/styles.css?v=20260909-connectivity-1',
  '/worker-ui.css?v=20260904-scan-safety',
  '/qr-scanner.css?v=20260904-persistent-zoom',
  '/connectivity-status.js?v=20260909-connectivity-1',
  '/qr-scanner.js?v=20260904-zoom-before-preview',
  '/app.js?v=20260904-scan-safety',
  '/article-colors.js?v=20260903-recognition-colors',
  '/process-mode.js?v=20260908-process-ui-2',
  '/process-mode-focus-fix.js?v=20260909-auth-grace-3',
  '/target-location.js?v=20260909-pcl-targets-1',
  '/process-shell.js?v=20260909-auth-grace-1',
  '/header-user-menu.js?v=20260909-header-user-2',
  '/process-ux-optimizations.js?v=20260909-process-ux-1',
  '/process-hotfix-20260909.js?v=20260909-tank-ux-2',
  '/personnel-auth.js?v=20260903-guided-worker',
  '/nfc.js?v=20260903-guided-worker',
  '/submit.js?v=20260903-guided-worker',
  '/worker-ui.js?v=20260904-scan-safety',
  '/worker-enhancements.js?v=20260904-worker-flow-3',
  '/manifest.webmanifest',
  '/icons/favicon.svg',
  '/icons/icon-192.svg',
  '/icons/icon-512.svg',
  '/icons/icon-maskable-512.svg'
];

self.addEventListener('install',e=>{
  e.waitUntil(caches.open(CACHE).then(c=>c.addAll(ASSETS)));
});

self.addEventListener('activate',e=>{
  e.waitUntil(caches.keys().then(keys=>Promise.all(keys.filter(k=>k.startsWith('fam-pulver-staging-')&&k!==CACHE).map(k=>caches.delete(k)))));
});

self.addEventListener('fetch',e=>{
  if(e.request.method==='GET'&&e.request.url.includes('/api/'))return;
  if(e.request.method!=='GET')return;

  if(e.request.mode==='navigate'){
    // Installed Android PWAs must paint the cached App Shell immediately. Waiting for a network
    // navigation here can leave only the native splash/logo visible until a TCP/browser timeout.
    // Refresh the cached shell in the background, but never use a cached API response as ERP truth.
    const network=fetch(e.request).then(async response=>{
      if(response.ok){
        const cache=await caches.open(CACHE);
        await cache.put('/',response.clone());
        await cache.put('/index.html',response.clone());
      }
      return response;
    });
    e.waitUntil(network.then(()=>undefined).catch(()=>undefined));
    e.respondWith(
      caches.match('/').then(cached=>cached||network)
        .catch(()=>caches.match('/index.html'))
    );
    return;
  }

  e.respondWith(caches.match(e.request).then(r=>r||fetch(e.request)));
});
