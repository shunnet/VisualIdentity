
const {chromium}=require("playwright");
const baseUrl="http://127.0.0.1:5206";
async function wf(fn,t,l){const s=Date.now();while(Date.now()-s<t){try{const v=await fn();if(v)return v;}catch(e){}await new Promise(r=>setTimeout(r,250));}throw new Error("timeout "+l);}
(async()=>{const b=await chromium.launch({headless:true,channel:"msedge"});const p=await b.newPage({viewport:{width:1440,height:900}});p.on("pageerror",e=>console.log("PAGEERROR: "+e.message));
try{
 await p.goto(baseUrl+"/login",{waitUntil:"domcontentloaded",timeout:30000}); await p.waitForTimeout(4000);
 await p.locator("input").first().fill("admin");await p.locator("input[type=password]").fill("123456");
 await p.locator("button",{hasText:"登录"}).first().click();
 await wf(async()=>!p.url().includes("/login"),15000,"in"); await p.waitForTimeout(800);
 await p.goto(baseUrl+"/project/fb62575b831f4d2a90bf1d44088161a6",{waitUntil:"domcontentloaded"}); await p.waitForTimeout(2200);
 console.log("rows now:", await p.locator(".table tbody tr").count());
 if ((await p.locator(".table tbody tr").count())===0){
   const fi=p.locator("#image-import-input"); await fi.setInputFiles(["F:/Snet/VisualIdentity/Snet.Yolo.Tasks/wwwroot/samples/demo.jpg"]);
   await wf(async()=>(await p.locator(".table tbody tr").count())>=1,15000,"tasks");
   console.log("rows after import:", await p.locator(".table tbody tr").count());
   await p.waitForTimeout(1000);
 }
 await p.goto(baseUrl+"/labeling/fb62575b831f4d2a90bf1d44088161a6/0",{waitUntil:"domcontentloaded"}); await p.waitForTimeout(3500);
 await p.screenshot({path:"F:/Snet/VisualIdentity/artifacts/review/09-editor-detect-data.png"}); console.log("shot 09-editor-detect-data");
}catch(e){console.log("ERR "+e.message.slice(0,300));}finally{await b.close();}})();