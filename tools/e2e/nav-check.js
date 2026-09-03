const {chromium}=require("playwright");
const baseUrl="http://127.0.0.1:5206";
const OUT="F:/Snet/VisualIdentity/artifacts/review";
async function wf(fn,t,l){const s=Date.now();while(Date.now()-s<t){try{const v=await fn();if(v)return v;}catch(e){}await new Promise(r=>setTimeout(r,250));}throw new Error("timeout "+l);}
(async()=>{
const b=await chromium.launch({headless:true,channel:"msedge"});
async function login(p){ await p.goto(baseUrl+"/login",{waitUntil:"domcontentloaded",timeout:30000}); await p.waitForTimeout(3800); await p.locator("input").first().fill("admin"); await p.locator("input[type=password]").fill("123456"); await p.locator("button",{hasText:"登录"}).first().click(); await wf(async()=>!p.url().includes("/login"),15000,"in"); await p.waitForTimeout(900); }
const p=await b.newPage({viewport:{width:1440,height:900}}); await login(p);
await p.goto(baseUrl+"/labeling/fb62575b831f4d2a90bf1d44088161a6/0",{waitUntil:"domcontentloaded"}); await p.waitForTimeout(3200);
console.log("side arrows:", await p.locator(".ls-nav-prev").count(), await p.locator(".ls-nav-next").count(), "| nav bar:", await p.locator(".ls-task-nav").count());
console.log("nav label:", (await p.locator(".ls-task-nav-label").first().textContent().catch(()=>"-")).trim());
await p.screenshot({path:OUT+"/F6-editor-nav.png"});
const m=await b.newPage({viewport:{width:390,height:844}}); await login(m);
await m.goto(baseUrl+"/labeling/fb62575b831f4d2a90bf1d44088161a6/0",{waitUntil:"domcontentloaded"}); await m.waitForTimeout(3000);
await m.screenshot({path:OUT+"/M-editor-nav.png"});
console.log("mobile arrows:", await m.locator(".ls-nav-prev").count(), await m.locator(".ls-task-nav").count());
await m.locator(".ls-task-nav button").first().click(); await m.waitForTimeout(900);
console.log("after prev click nav label:", (await m.locator(".ls-task-nav-label").first().textContent().catch(()=>"-")).trim());
console.log("DONE");
})().catch(e=>console.log("ERR "+e.message.slice(0,300))).finally(()=>b.close());