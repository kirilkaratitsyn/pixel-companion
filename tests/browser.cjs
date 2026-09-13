// UI contract tests with an explicitly simulated WebSocket peer, independent of Windows GSMTC.
const { chromium } = require(process.env.PLAYWRIGHT_PATH || 'playwright');
const http = require('node:http'), fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const root = path.resolve(__dirname,'..');
const server = http.createServer((req,res)=>{
 if(req.url==='/api/pair') { let body=''; req.on('data',d=>body+=d);req.on('end',()=>{const ok=JSON.parse(body).code==='123456';res.writeHead(ok?200:401,{'Content-Type':'application/json'});res.end(JSON.stringify(ok?{token:'a'.repeat(64)}:{error:'Неверный код'}));}); return; }
 const file = {'/':'index.html','/app.js':'app.js','/style.css':'style.css','/mixer.css':'mixer.css'}[req.url];
 if(!file){res.writeHead(404);res.end();return;}res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html');res.end(fs.readFileSync(path.join(root,'web',file)));
});
(async()=>{
 await new Promise(r=>server.listen(19765,'127.0.0.1',r));
 const browser=await chromium.launch({headless:true,...(process.env.CHROME_CHANNEL?{channel:process.env.CHROME_CHANNEL}:{})});
 const intervals=[];
 try{
  const page=await browser.newPage({viewport:{width:860,height:396}}),errors=[],commands=[];page.on('pageerror',e=>errors.push(e.message));
  const state={type:'state',version:1,computer:'Тестовый ПК',locked:false,timestamp:Date.now(),selectedSource:null,sources:[{id:'test',name:'Тестовый плеер'}],volume:.35,mixer:[{id:'chrome',name:'Google Chrome',volume:.72,muted:false,active:true},{id:'discord',name:'Discord',volume:.4,muted:false,active:false}],shortcuts:[{id:'discord',name:'Discord',kind:'app'},{id:'youtube-music',name:'YouTube Music',kind:'music'},{id:'spotify',name:'Spotify',kind:'music'}],queue:[{id:'yt-track-1',title:'Первый трек',artist:'Исполнитель',active:true},{id:'yt-track-2',title:'Следующий трек из очереди',artist:'Исполнитель',active:false}],error:null,media:{sessionId:'test-session',revision:1,source:'Тестовый плеер',title:'Тихий свет',artist:'Тест интерфейса',status:'playing',position:84,duration:222,rate:1,artworkHash:null,controls:{play:true,pause:true,toggle:true,previous:true,next:true,seek:true}}};
  let route,allow=true;
  await page.routeWebSocket('ws://127.0.0.1:19765/ws',r=>{route=r;r.onMessage(raw=>{const m=JSON.parse(raw);if(m.type==='auth'){if(!allow){r.close();return;}r.send(JSON.stringify({type:'ready',version:1}));r.send(JSON.stringify(state));intervals.push(setInterval(()=>{try{r.send(JSON.stringify(state));}catch{}},500));return;}commands.push(m);if(m.name==='toggle')state.media.status=state.media.status==='playing'?'paused':'playing';if(m.name==='next'){state.media.title='Следующий трек';state.media.revision++;}if(m.name==='seek')state.media.position=m.value;if(m.name==='volume')state.volume=m.value;if(m.name==='mixer-volume')state.mixer.find(x=>x.id===m.mixerId).volume=m.value;if(m.name==='mixer-mute')state.mixer.find(x=>x.id===m.mixerId).muted=m.muted;r.send(JSON.stringify({type:'result',id:m.id,ok:true}));r.send(JSON.stringify(state));});});
  await page.goto('http://127.0.0.1:19765');
  await page.locator('#pair-code').fill('000000');await page.locator('#pair-button').click();await page.waitForFunction(()=>document.getElementById('pair-error').textContent.includes('Неверный'));
  await page.locator('#pair-code').fill('123456');await page.locator('#pair-button').click();await page.locator('#music').waitFor({state:'visible'});
  assert.equal(await page.locator('#status').isVisible(),false);assert.equal(await page.locator('#source-name').isVisible(),false);assert.equal(await page.locator('#connection-name').isVisible(),false);
  await page.locator('#deck').click();await page.locator('.deck-tile',{hasText:'Discord'}).click();assert.ok(commands.some(m=>m.name==='launch'&&m.shortcutId==='discord'));if(process.env.TEST_OUTPUT)await page.screenshot({path:path.join(process.env.TEST_OUTPUT,'app-deck.png')});await page.locator('#close').click();
  await page.locator('#deck').click();await page.locator('.deck-tile',{hasText:'YouTube Music'}).click();if(process.env.TEST_OUTPUT)await page.screenshot({path:path.join(process.env.TEST_OUTPUT,'app-music.png')});await page.locator('.music-list .row',{hasText:'Следующий трек из очереди'}).click();assert.ok(commands.some(m=>m.name==='browser-play'&&m.browserTrackId==='yt-track-2'));
  await page.locator('#play').click();await page.waitForFunction(()=>document.getElementById('status').textContent==='На паузе');
  await page.locator('#next').click();await page.waitForFunction(()=>document.getElementById('title').textContent==='Следующий трек');
  await page.locator('#seek').fill('500');await page.locator('#seek').dispatchEvent('change');assert.ok(commands.some(m=>m.name==='seek'&&m.value===111));
  await page.locator('#volume-open').click();await page.locator('#volume-slider').fill('61');await page.locator('#volume-slider').dispatchEvent('change');
  assert.equal(await page.locator('.mixer-item').count(),2);const chrome=page.locator('.mixer-item[data-mixer-id="chrome"]');await chrome.locator('.mixer-slider').fill('44');await chrome.locator('.mixer-slider').dispatchEvent('change');await chrome.locator('.mixer-mute').click();
  assert.ok(commands.some(m=>m.name==='mixer-volume'&&m.mixerId==='chrome'&&m.value===.44));assert.ok(commands.some(m=>m.name==='mixer-mute'&&m.mixerId==='chrome'&&m.muted===true));
  if(process.env.TEST_OUTPUT)await page.screenshot({path:path.join(process.env.TEST_OUTPUT,'app-mixer.png')});
  await page.locator('#close').click();await page.waitForFunction(()=>document.getElementById('volume-value').textContent==='61%');
  await page.locator('#play').click();await page.waitForFunction(()=>document.getElementById('status').textContent==='Сейчас играет');
  await page.locator('#settings').click();assert.equal(await page.locator('#brightness').getAttribute('max'),'100');assert.equal(await page.locator('#idle-delay').inputValue(),'0');assert.equal(await page.locator('#usb-side').inputValue(),'left');await page.locator('#blank-now').click();await page.waitForTimeout(700);assert.equal(await page.locator('#wake').isVisible(),true);
  const count=commands.length;await page.locator('#wake').click();assert.equal(commands.length,count);assert.equal(state.media.status,'playing');
  state.media.controls.seek=false;state.media.controls.next=false;route.send(JSON.stringify(state));await page.waitForFunction(()=>document.getElementById('next').disabled);assert.equal(await page.locator('#seek').isDisabled(),true);
  state.media.title='<img src=x onerror=alert(1)>';route.send(JSON.stringify(state));await page.waitForFunction(()=>document.getElementById('title').textContent.startsWith('<img'));assert.equal(await page.locator('#title img').count(),0);
  state.media.title='Тихий свет';state.media.controls.seek=true;state.media.controls.next=true;route.send(JSON.stringify(state));
  if(process.env.TEST_OUTPUT){fs.mkdirSync(process.env.TEST_OUTPUT,{recursive:true});await page.screenshot({path:path.join(process.env.TEST_OUTPUT,'app-landscape.png')});}
  state.locked=true;route.send(JSON.stringify(state));await page.locator('#wake').waitFor({state:'visible'});await page.locator('#wake').click();assert.equal(commands.length,count);
  state.locked=false;state.media.status='none';state.media.sessionId=null;route.send(JSON.stringify(state));await page.locator('#empty').waitFor({state:'visible'});
  allow=false;intervals.forEach(clearInterval);route.close();await page.waitForFunction(()=>document.getElementById('volume-open').disabled);
  await page.setViewportSize({width:320,height:720});const size=await page.evaluate(()=>({client:document.documentElement.clientWidth,scroll:document.documentElement.scrollWidth}));assert.ok(size.scroll<=size.client+1);
  await page.locator('#settings').click();if(process.env.TEST_OUTPUT)await page.screenshot({path:path.join(process.env.TEST_OUTPUT,'app-settings.png')});
  assert.deepEqual(errors,[]);
  console.log(JSON.stringify({passed:true,checks:['pairing error/success','simplified player labels','quick launch deck','YouTube Music queue selection','play/pause','next','seek','master volume','per-app mixer volume/mute','100% brightness','USB side setting','never-sleep default','manual black screen persists','wake does not send command','unsupported controls','untrusted metadata rendered as text','PC lock','no session','offline controls','320px layout'],backend:'simulated WebSocket contract, not Windows'},null,2));
 }finally{intervals.forEach(clearInterval);await browser.close();server.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
