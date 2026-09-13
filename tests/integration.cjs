// node tests/integration.cjs <agent-data-directory> <fixture-data-directory>
// Requires the real Windows agent --loopback --port 18765 and --media-fixture process.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const [agentDir, fixtureDir] = process.argv.slice(2);
const ready = JSON.parse(fs.readFileSync(path.join(agentDir, 'ready.json')));
const base = `http://127.0.0.1:${ready.port}`;
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
const sockets = [];
async function connection(token, expected = 'ready') {
  const ws = new WebSocket(base.replace('http:', 'ws:') + '/ws'); sockets.push(ws);
  const messages = []; ws.onmessage = e => messages.push(JSON.parse(e.data));
  await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });
  ws.send(JSON.stringify({type:'auth', token}));
  const wait = async (predicate, timeout=9000) => { const start=Date.now(); while(Date.now()-start<timeout){const i=messages.findIndex(predicate);if(i>=0)return messages.splice(i,1)[0];await delay(30);}throw Error('Timed out waiting for server message; last states: '+JSON.stringify(messages.slice(-3))); };
  await wait(m=>m.type===expected);
  return {ws,wait,messages};
}
(async()=>{
  const checks=[];
  try {
    assert.equal((await (await fetch(base+'/health')).json()).version,1);checks.push('health');
    const pair = (code,origin) => fetch(base+'/api/pair',{method:'POST',headers:{'Content-Type':'application/json',...(origin?{Origin:origin}:{})},body:JSON.stringify({code})});
    assert.equal((await pair(ready.code,'http://evil.invalid')).status,403); checks.push('cross-origin pairing rejected');
    assert.equal((await pair('000000')).status,401); checks.push('incorrect PIN rejected');
    const response = await pair(ready.code); assert.equal(response.status,200); const {token}=await response.json();assert.equal(token.length,64);
    fs.writeFileSync(path.join(agentDir,'test-token.txt'),token);
    assert.equal((await pair(ready.code)).status,401);checks.push('single-use pairing');
    const invalid=await connection('0'.repeat(64),'auth_error');invalid.ws.close();checks.push('invalid WS token rejected');
    const c = await connection(token);
    let snapshot = await c.wait(m=>m.type==='state' && m.sources.some(s=>s.id.toLowerCase().includes('pixelcompanion')));
    const fixtureSource = snapshot.sources.find(s=>s.id.toLowerCase().includes('pixelcompanion')).id;
    const selectId=crypto.randomUUID();c.ws.send(JSON.stringify({id:selectId,name:'source',sourceId:fixtureSource}));
    assert.equal((await c.wait(m=>m.type==='result'&&m.id===selectId)).ok,true);
    snapshot = await c.wait(m=>m.type==='state' && m.selectedSource===fixtureSource && m.media.title.startsWith('Pixel integration track'));
    assert.ok(snapshot.media.sessionId);checks.push('real Windows GSMTC session discovered');
    async function command(name,extra={},id=crypto.randomUUID()){
      c.ws.send(JSON.stringify({id,name,sessionId:snapshot.media.sessionId,revision:snapshot.media.revision,...extra}));
      return c.wait(m=>m.type==='result'&&m.id===id);
    }
    assert.equal((await command('pause')).ok,true);
    snapshot=await c.wait(m=>m.type==='state'&&m.media.status==='paused'&&m.media.title.startsWith('Pixel integration track'));
    assert.equal(fs.readFileSync(path.join(fixtureDir,'fixture-command.txt'),'utf8'),'Pause');checks.push('native pause command and callback');
    assert.equal((await command('play')).ok,true);await c.wait(m=>m.type==='state'&&m.media.status==='playing');checks.push('native play');
    const old=snapshot.media;
    const nextId=crypto.randomUUID();assert.equal((await command('next',{},nextId)).ok,true);
    snapshot=await c.wait(m=>m.type==='state'&&m.media.title==='Pixel integration track 2');
    c.ws.send(JSON.stringify({id:nextId,name:'next',sessionId:old.sessionId,revision:old.revision}));
    assert.equal((await c.wait(m=>m.type==='result'&&m.id===nextId)).ok,true);
    await delay(700);assert.ok(!c.messages.some(m=>m.type==='state'&&m.media.title==='Pixel integration track 3'));checks.push('native next and duplicate command idempotency');
    assert.equal((await command('next',{sessionId:old.sessionId,revision:old.revision})).ok,false);checks.push('stale track command rejected');
    if(snapshot.media.controls.seek){assert.equal((await command('seek',{value:90})).ok,true);await delay(150);assert.equal(fs.readFileSync(path.join(fixtureDir,'fixture-seek.txt'),'utf8'),'90');checks.push('native seek');}
    assert.equal((await command('seek',{value:-1})).ok,false);checks.push('invalid seek rejected');
    if(snapshot.volume!=null){assert.equal((await command('volume',{value:snapshot.volume})).ok,true);checks.push('Core Audio same-level volume command');}
    const reconnect=await connection(token);await reconnect.wait(m=>m.type==='state');checks.push('saved token reconnect');
    fs.writeFileSync(path.join(agentDir,'test-token.txt'),token);
    console.log(JSON.stringify({passed:true,checks,seekSupported:snapshot.media.controls.seek,artworkPresent:!!snapshot.media.artworkHash},null,2));
  } finally {for(const ws of sockets)ws.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
