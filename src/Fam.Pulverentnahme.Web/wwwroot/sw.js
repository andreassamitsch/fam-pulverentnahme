const CACHE='fam-pulver-v45-user-idle-timeout-20261005';
const ASSETS=[
  '/',
  '/index.html',
  '/styles.css?v=20260909-connectivity-1',
  '/worker-ui.css?v=20261002-service-config-1',
  '/qr-scanner.css?v=20260904-persistent-zoom',
  '/ui-config.js?v=20261005-user-timeout-1',
  '/connectivity-status.js?v=20260909-connectivity-1',
  '/qr-scanner.js?v=20260904-zoom-before-preview',
  '/app.js?v=20261002-dynamic-tanks-1',
  '/ui-diagnostics.js?v=20261005-label-reprint-diag-1',
  '/article-colors.js?v=20260909-single-process-mode',
  '/personnel-auth.js?v=20261005-user-timeout-1',
  '/nfc.js?v=20260903-guided-worker',
  '/submit.js?v=20260903-guided-worker',
  '/worker-ui.js?v=20261001-operator-ui-1',
  '/worker-enhancements.js?v=20260904-worker-flow-3',
  '/process-mode.js?v=20261005-inventory-speed-colors-1',
  '/process-mode-focus-fix.js?v=20261001-operator-ui-1',
  '/replenish-router-guard.js?v=20260909-replenish-guard-1',
  '/target-location.js?v=20260909-pcl-targets-1',
  '/process-shell.js?v=20261001-jobabort-colors-1',
  '/header-user-menu.js?v=20260909-header-user-2',
  '/process-ux-optimizations.js?v=20261001-operator-ui-1',
  '/process-hotfix-20260909.js?v=20261001-operator-ui-1',
  '/manifest.webmanifest',
  '/icons/fam-pulver-master.svg',
  '/icons/favicon.svg',
  '/icons/favicon-32.png',
  '/icons/icon-192.png',
  '/icons/icon-512.png',
  '/icons/icon-maskable-512.png',
  '/apple-touch-icon.png'
];

self.addEventListener('install',e=>{
  // Force a network revalidation of static assets when a new app-shell version is installed.
  // This avoids an old HTTP-cache entry being copied into a new Cache Storage generation under
  // the same versioned asset URL.
  e.waitUntil(caches.open(CACHE).then(c=>c.addAll(ASSETS.map(url=>new Request(url,{cache:'reload'})))));
});

self.addEventListener('activate',e=>{
  e.waitUntil(caches.keys().then(keys=>Promise.all(keys.filter(k=>k.startsWith('fam-pulver-')&&k!==CACHE).map(k=>caches.delete(k)))));
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
