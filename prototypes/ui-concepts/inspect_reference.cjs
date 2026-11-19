const fs = require('node:fs');
const path = require('node:path');
const {spawn} = require('node:child_process');
const {pathToFileURL} = require('node:url');
const delay = ms => new Promise(r=>setTimeout(r,ms));
const workspace = path.resolve(__dirname,'..','..');
const output = path.join(workspace,'design-system','notifs','reference','screens');
fs.mkdirSync(output,{recursive:true});
const edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',[
  '--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check',
  '--remote-debugging-port=9241','--remote-allow-origins=http://localhost:9241',
  '--user-data-dir='+path.join(workspace,'.scratch','edge-design-qa'),'about:blank'
],{windowsHide:true,stdio:'ignore'});
let ws;
(async()=>{
  let pages;
  for(let i=0;i<50;i++){try{pages=await(await fetch('http://localhost:9241/json/list')).json();break;}catch{await delay(100);}}
  if(!pages)throw new Error('Preview browser did not start');
  ws=new WebSocket(pages.find(p=>p.type==='page').webSocketDebuggerUrl);
  await new Promise((r,j)=>{ws.addEventListener('open',r,{once:true});ws.addEventListener('error',j,{once:true});});
  let sequence=0,contexts=[],errors=[];const pending=new Map();
  ws.addEventListener('message',event=>{
    const message=JSON.parse(event.data);
    if(message.id){const callbacks=pending.get(message.id);pending.delete(message.id);if(message.error)callbacks.reject(message.error);else callbacks.resolve(message.result);}
    else if(message.method==='Runtime.executionContextCreated')contexts.push({...message.params.context,session:message.sessionId});
    else if(message.method==='Runtime.executionContextDestroyed')contexts=contexts.filter(c=>c.id!==message.params.executionContextId||c.session!==message.sessionId);
    else if(message.method==='Target.attachedToTarget')send('Runtime.enable',{},message.params.sessionId);
    else if(message.method==='Runtime.executionContextsCleared')contexts=[];
    else if(message.method==='Runtime.exceptionThrown')errors.push(message.params.exceptionDetails.text+' '+(message.params.exceptionDetails.exception?.description??''));
  });
  function send(method,params={},sessionId){const id=++sequence;return new Promise((resolve,reject)=>{pending.set(id,{resolve,reject});ws.send(JSON.stringify({id,method,params,...(sessionId?{sessionId}:{})}));});}
  await send('Runtime.enable');await send('Page.enable');
  await send('Target.setAutoAttach',{autoAttach:true,waitForDebuggerOnStart:false,flatten:true});
  await send('Emulation.setDeviceMetricsOverride',{width:1060,height:1240,deviceScaleFactor:1,mobile:false});
  await send('Page.addScriptToEvaluateOnNewDocument',{source:'try{localStorage.clear()}catch{};Date.now=()=>Date.UTC(2026,10,18,12)'}); await send('Network.enable'); await send('Network.setBlockedURLs',{urls:['http://*','https://*']});
  await send('Page.navigate',{url:pathToFileURL(path.join(workspace,'prototypes','ui-concepts','notifs-reference.html')).href});
  let context;
  for(let i=0;i<100;i++){
    for(const candidate of contexts){try{const test=await send('Runtime.evaluate',{contextId:candidate.id,expression:"!!document.getElementById('notifs-next')?.querySelector('.nx-brand')",returnByValue:true},candidate.session);if(test.result.value)context=candidate;}catch{}}
    if(context)break;await delay(100);
  }
  if(!context){
    console.log(JSON.stringify({contexts,errors}));
    const image=await send('Page.captureScreenshot',{format:'png'});fs.writeFileSync(path.join(output,'failed-preview.png'),Buffer.from(image.data,'base64'));
    for(const candidate of contexts){try{console.log(JSON.stringify(await send('Runtime.evaluate',{contextId:candidate.id,expression:"({url:location.href,text:document.body.innerText.slice(0,1200)})",returnByValue:true})));}catch{}}
    throw new Error('Prototype did not render');
  }
  async function evaluate(expression){const r=await send('Runtime.evaluate',{contextId:context.id,expression,returnByValue:true,awaitPromise:true},context.session);if(r.exceptionDetails)throw new Error(r.exceptionDetails.exception?.description??r.exceptionDetails.text);return r.result.value;}
  await delay(500);
  const screenshot=async name=>{await delay(250);const viewportWidth=await evaluate("innerWidth"),captureHeight=await evaluate("Math.ceil(document.querySelector('.nx-app').getBoundingClientRect().bottom)+8");const r=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true,clip:{x:0,y:0,width:viewportWidth,height:captureHeight,scale:1}});fs.writeFileSync(path.join(output,name+'.png'),Buffer.from(r.data,'base64'));};
  const results=await require('./reference-scenarios.cjs')({evaluate,send,screenshot,delay});
  const contrast=await evaluate(`(()=>{const app=document.querySelector('.nx-app'),css=getComputedStyle(app);const lum=k=>{const v=css.getPropertyValue(k).trim().slice(1);const a=[0,2,4].map(i=>parseInt(v.slice(i,i+2),16)/255).map(x=>x<=.04045?x/12.92:((x+.055)/1.055)**2.4);return a[0]*.2126+a[1]*.7152+a[2]*.0722;};const pairs=[['--nx-ink','--nx-bg'],['--nx-muted','--nx-rail'],['--nx-muted','--nx-panel'],['--nx-muted','--nx-selected'],['--nx-on','--nx-accent'],...['s','a','b','c','d'].map(r=>['--nx-'+r+'-ink','--nx-'+r])];return pairs.map(([a,b])=>({pair:a+'/'+b,ratio:Math.round((Math.max(lum(a),lum(b))+.05)/(Math.min(lum(a),lum(b))+.05)*100)/100}));})()`);
  console.log(JSON.stringify({errors,results,contrast},null,2));
  fs.writeFileSync(path.join(output,'inspection.json'),JSON.stringify({errors,results,contrast},null,2));
  if(errors.length||results.some(r=>!r.ok)||contrast.some(p=>p.ratio<4.5))process.exitCode=1;
  ws.close();edge.kill();
})().catch(error=>{console.error(error);ws?.close();edge.kill();process.exitCode=1;});
