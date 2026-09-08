const CACHE='fam-pulver-staging-v23-header-user-menu';
const ASSETS=[
  '/',
  '/index.html',
  '/styles.css?v=20260904-scan-safety',
  '/worker-ui.css?v=20260904-scan-safety',
  '/qr-scanner.css?v=20260904-persistent-zoom',
  '/qr-scanner.js?v=20260904-zoom-before-preview',
  '/app.js?v=20260904-scan-safety',
  '/article-colors.js?v=20260903-recognition-colors',
  '/process-mode.js?v=20260908-process-ui-2',
  '/process-mode-focus-fix.js?v=20260908-focus-stable-2',
  '/target-location.js?v=20260908-location-ajax-2',
  '/process-shell.js?v=20260908-process-shell-1',
  '/header-user-menu.js?v=20260908-header-user-1',
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
    e.respondWith(fetch(e.request).then(response=>{const copy=response.clone();caches.open(CACHE).then(c=>c.put('/',copy));return response}).catch(()=>caches.match(e.request).then(r=>r||caches.match('/'))));
    return;
  }
  e.respondWith(caches.match(e.request).then(r=>r||fetch(e.request)));
});
