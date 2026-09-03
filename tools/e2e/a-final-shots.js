
const {chromium}=require("playwright");
const baseUrl="http://127.0.0.1:5206";
const OUT="F:/Snet/VisualIdentity/artifacts/review";
async function wf(fn,t,l){const s=Date.now();while(Date.now()-s<t){try{const v=await fn();if(v)return v;}catch(e){}await new Promise(r=>setTimeout(r,250));}throw new Error("timeout "+l);}
(async()=>{const b=await chromium.launch({headless:true,channel:"msedge"});
async function login(p){ await p.goto(baseUrl+"/login",{waitUntil:"domcontentloaded",timeout:30000}); await p.waitForTimeout(3800);
 await p.locator("input").first().fill("admin");await p.locator("input[type=password]").fill("123456");
 await p.locator("button",{hasText:"登录"}).first().click(); await wf(async()=>!p.url().includes("/login"),15000,"in"); await p.waitForTimeout(900); }
try{
 // desktop
 const p=await b.newPage({viewport:{width:1440,height:900}}); p.on("pageerror",e=>console.log("PAGEERROR: "+e.message));
 await login(p);
 const shots=[["/projects","F1-projects"],["/project/de01917b58164dfabfb9cbc0677df8e0","F2-classify"],["/validation","F3-val"],["/users","F4-users"],["/train/de01917b58164dfabfb9cbc0677df8e0","F5-train"],["/labeling/fb62575b831f4d2a90bf1d44088161a6/0","F6-editor"]];
 for (const [u,n] of shots) { await p.goto(baseUrl+u,{waitUntil:"domcontentloaded"}); await p.waitForTimeout(2400); await p.screenshot({path:OUT+"/"+n+".png"}); console.log("shot",n); }
 // editor drawer mobile
 const m=await b.newPage({viewport:{width:390,height:844}}); await login(m);
 await m.goto(baseUrl+"/labeling/fb62575b831f4d2a90bf1d44088161a6/0",{waitUntil:"domcontentloaded"}); await m.waitForTimeout(3000);
 await m.screenshot({path:OUT+"/M-editor.png"}); console.log("shot M-editor");
 console.log("panel btns:", await m.locator(".ls-mobile-panel-btn").count());
 await m.locator(".ls-mobile-panel-left").first().click(); await m.waitForTimeout(600);
 await m.screenshot({path:OUT+"/M-editor-left-open.png"});
 console.log("left open:", await m.locator(".ls-labeling-left").evaluate(el=>el.className));
 await m.locator(".ls-labeling-backdrop").first().click().catch(e=>console.log("backdrop click err:",e.message.slice(0,60)));
 await m.waitForTimeout(500);
 await m.locator(".ls-mobile-panel-right").first().click(); await m.waitForTimeout(600);
 await m.screenshot({path:OUT+"/M-editor-right-open.png"});
 console.log("right open:", await m.locator(".ls-labeling-right").evaluate(el=>el.className));
 // mobile remainder
 for (const [u,n] of [["/projects","M2-projects"],["/validation","M3-val"],["/users","M4-users"]]) { await m.goto(baseUrl+u,{waitUntil:"domcontentloaded"}); await m.waitForTimeout(2000); await m.screenshot({path:OUT+"/"+n+".png"}); console.log("shot",n); }
 console.log("ALL DONE");
}catch(e){console.log("ERR "+e.message.slice(0,400));}finally{await b.close();}})();