import assert from 'node:assert/strict';
import test from 'node:test';
import {readFile} from 'node:fs/promises';
import {createServer} from 'node:http';
import {chromium} from 'playwright';

test('SAM tools intercept clicks, block concurrent prompts, render masks, and restore manual drawing', {timeout:60000}, async()=>{
  const root=new URL('../../Snet.Yolo.Tasks.Shared/wwwroot/js/',import.meta.url);
  const source=await readFile(new URL('ls-canvas.js',root)), helper=await readFile(new URL('polygon-path.js',root));
  const fixture=`<!doctype html><style>body{margin:0}#host{width:524px;height:524px}</style><div class='ls-labeling'><div id='host'><canvas id='canvas'></canvas></div><button id='other' onclick='window.otherClicks=(window.otherClicks||0)+1'>Other</button><button id='cancel' data-sam-cancel onclick='window.cancelClicks=(window.cancelClicks||0)+1'>Cancel</button></div>
    <script type='module'>
    import * as engine from '/ls-canvas.js';window.engine=engine;window.events=[];
    const image='data:image/svg+xml,'+encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="500" height="500"><rect width="500" height="500" fill="#18212c"/></svg>');
    await engine.init('canvas',image,{async invokeMethodAsync(name,...args){
      // 与 Editor.OnKey(string,bool,bool,bool) 保持同样的互操作参数约束。
      if(name==='OnKey'&&(args.length!==4||typeof args[0]!=='string'||args.slice(1).some(v=>typeof v!=='boolean')))throw new Error('OnKey requires action, ctrl, shift, alt');
      window.events.push({name,args});if(name==='OnSamPoint')await new Promise(r=>setTimeout(r,120));
    }});
    const mask=document.createElement('canvas');mask.width=500;mask.height=500;const c=mask.getContext('2d');c.fillStyle='#40a0ff';c.fillRect(100,100,200,200);window.mask=mask.toDataURL('image/png');
    window.ready=true;</script>`;
  const server=createServer((req,res)=>{res.setHeader('Content-Type',req.url.endsWith('.js')?'text/javascript':'text/html');res.end(req.url==='/ls-canvas.js'?source:req.url==='/polygon-path.js'?helper:fixture);});
  await new Promise(r=>server.listen(0,'127.0.0.1',r));let browser;
  try{
    browser=await chromium.launch({headless:true});const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
    await page.goto('http://127.0.0.1:'+server.address().port);await page.waitForFunction(()=>window.ready&&document.querySelector('canvas').dataset.imgLoaded==='1');
    await page.evaluate(()=>{
      window.engine.saveSamPreferences('sam-test:user-a',{model:2,enabled:true,gpuId:3});
      window.engine.saveSamPreferences('sam-test:user-b',{model:0,enabled:false,gpuId:null});
      window.engine.saveSamPreferences('sam-test:vit-l',{model:3,enabled:true,gpuId:null});
      window.engine.saveSamPreferences('sam-test:vit-h',{model:4,enabled:true,gpuId:0});
    });
    await page.reload();await page.waitForFunction(()=>window.ready&&document.querySelector('canvas').dataset.imgLoaded==='1');
    assert.deepEqual(await page.evaluate(()=>window.engine.loadSamPreferences('sam-test:user-a')),{model:2,enabled:true,gpuId:3});
    assert.deepEqual(await page.evaluate(()=>window.engine.loadSamPreferences('sam-test:user-b')),{model:0,enabled:false,gpuId:null});
    assert.deepEqual(await page.evaluate(()=>window.engine.loadSamPreferences('sam-test:vit-l')),{model:3,enabled:true,gpuId:null});
    assert.deepEqual(await page.evaluate(()=>window.engine.loadSamPreferences('sam-test:vit-h')),{model:4,enabled:true,gpuId:0});
    assert.equal(await page.evaluate(()=>{localStorage.setItem('sam-test:bad','{broken');return window.engine.loadSamPreferences('sam-test:bad');}),null);
    assert.equal(await page.evaluate(()=>{localStorage.setItem('sam-test:bad',JSON.stringify({model:0,enabled:true,gpuId:-1}));return window.engine.loadSamPreferences('sam-test:bad');}),null);
    assert.equal(await page.evaluate(()=>{localStorage.setItem('sam-test:bad',JSON.stringify({model:0,enabled:true,gpuId:2147483648}));return window.engine.loadSamPreferences('sam-test:bad');}),null);
    const count=()=>page.evaluate(()=>window.events.filter(e=>e.name==='OnSamPoint').length);
    const settle=()=>page.waitForTimeout(160);
    for(const tool of ['rect','polygon','brush']){
      await page.evaluate(tool=>{window.engine.setMode('canvas',tool);window.engine.pushState('canvas',{samEnabled:true,samBusy:false,samPreview:null});},tool);
      const before=await count();await page.mouse.click(212,212);await page.mouse.click(222,222);await settle();assert.equal(await count(),before+1);
      await page.keyboard.down('Shift');await page.mouse.click(112,112);await page.keyboard.up('Shift');await settle();
      assert.equal(await page.evaluate(()=>window.events.filter(e=>e.name==='OnSamPoint').at(-1).args[2]),false);
      await page.evaluate(()=>window.engine.pushState('canvas',{samEnabled:true,samBusy:true}));const busy=await count();await page.mouse.click(212,212);await settle();assert.equal(await count(),busy);
    }
    assert.equal(await page.evaluate(()=>window.events.filter(e=>['OnRectDrawn','OnPolygonFinished','OnBrushStroke'].includes(e.name)).length),0);
    await page.locator('#other').click();assert.equal(await page.evaluate(()=>window.otherClicks||0),0);
    assert.ok(await page.evaluate(()=>window.events.some(e=>e.name==='OnSamBusy')));
    const notices=await page.evaluate(()=>window.events.filter(e=>e.name==='OnSamBusy').length);
    await page.evaluate(()=>{for(let i=0;i<10;i++)document.querySelector('#other').click();});await settle();
    assert.ok(await page.evaluate(()=>window.events.filter(e=>e.name==='OnSamBusy').length)<=notices+1);
    await page.locator('#cancel').click();assert.equal(await page.evaluate(()=>window.cancelClicks),1);
    await page.evaluate(()=>window.engine.pushState('canvas',{samEnabled:true,samBusy:false,samPreview:{tool:'brush',dataUrl:window.mask,pointsX:[100,300,300,100],pointsY:[100,100,300,300]}}));
    await settle();const color=await page.evaluate(()=>{const c=document.querySelector('canvas');return [...c.getContext('2d').getImageData(318,318,1,1).data];});assert.ok(color[2]>80);
    await page.keyboard.press('Enter');await settle();assert.deepEqual(await page.evaluate(()=>window.events.at(-1).args),['sam-confirm',false,false,false]);
    await page.keyboard.press('Escape');await settle();assert.deepEqual(await page.evaluate(()=>window.events.at(-1).args),['sam-cancel',false,false,false]);
    await page.evaluate(()=>{window.engine.setMode('canvas','select');window.engine.pushState('canvas',{samEnabled:false,samPreview:null,regions:[{id:'mask',type:'brushlabels',maskDataUrl:window.mask,pointsX:[100,300,300,100],pointsY:[100,100,300,300]}]});});
    await page.mouse.move(212,212);await page.mouse.down();await page.mouse.move(242,242);await page.mouse.up();await settle();
    assert.equal(await page.evaluate(()=>window.events.filter(e=>e.name==='OnShapeMoved').length),0);
    await page.evaluate(()=>window.engine.setMode('canvas','rect'));await page.mouse.move(62,62);await page.mouse.down();await page.mouse.move(112,112);await page.mouse.up();await settle();
    assert.ok(await page.evaluate(()=>window.events.some(e=>e.name==='OnRectDrawn')));
    await page.evaluate(()=>{window.engine.pushState('canvas',{samEnabled:true,samBusy:true});window.engine.destroy('canvas');});
    await page.locator('#other').click();assert.equal(await page.evaluate(()=>window.otherClicks),1);
    await page.mouse.click(212,212);await settle();assert.deepEqual(errors,[]);
  }finally{if(browser)await browser.close();await new Promise(r=>server.close(r));}
});
