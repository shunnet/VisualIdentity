const {chromium}=require("playwright");
const baseUrl="http://127.0.0.1:5206";
async function wf(fn,t,l){const s=Date.now();while(Date.now()-s<t){try{const v=await fn();if(v)return v;}catch(e){}await new Promise(r=>setTimeout(r,250));}throw new Error("timeout "+l);}
(async()=>{
const b=await chromium.launch({headless:true,channel:"msedge"});
const p=await b.newPage({viewport:{width:1440,height:900}}); p.on("pageerror",e=>console.log("PAGEERROR:",e.message));
await p.goto(baseUrl+"/login",{waitUntil:"domcontentloaded",timeout:30000}); await p.waitForTimeout(3800);
await p.locator("input").first().fill("admin"); await p.locator("input[type=password]").fill("123456");
await p.locator("button",{hasText:"登录"}).first().click(); await wf(async()=>!p.url().includes("/login"),15000,"in"); await p.waitForTimeout(900);
await p.goto(baseUrl+"/train/de01917b58164dfabfb9cbc0677df8e0",{waitUntil:"domcontentloaded"}); await p.waitForTimeout(2600);
await p.locator(".ls-card", {hasText:"训练输出"}).locator("button.ls-icon-btn").first().click(); await p.waitForTimeout(800);
console.log("copy toast:", JSON.stringify((await p.locator(".ls-toast").allTextContents()).map(s=>s.trim())));
const res = await p.evaluate(async () => { const el=document.querySelector(".ls-terminal"); for(let i=0;i<40;i++){const d=document.createElement("div"); d.className="ls-term-line"; d.textContent="line "+i; el.appendChild(d);} await new Promise(r=>setTimeout(r,400)); return el.scrollHeight-el.scrollTop-el.clientHeight; });
console.log("autoscroll delta:", res);
await p.screenshot({path:"F:/Snet/VisualIdentity/artifacts/review/train-terminal-final.png"});
console.log("DONE");
})().catch(e=>console.log("ERR "+e.message.slice(0,300))).finally(()=>b.close());