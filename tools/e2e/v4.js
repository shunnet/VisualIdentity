
const {chromium}=require("playwright");
const baseUrl="http://127.0.0.1:5206";
async function wf(fn,t,l){const s=Date.now();while(Date.now()-s<t){try{const v=await fn();if(v)return v;}catch(e){}await new Promise(r=>setTimeout(r,200));}throw new Error("timeout "+l);}
(async()=>{const b=await chromium.launch({headless:true,channel:"msedge"});const p=await b.newPage({viewport:{width:1500,height:850}});
try{
 await p.goto(baseUrl+"/",{waitUntil:"domcontentloaded",timeout:30000});
 await wf(async()=>p.url().includes("/login"),10000,"l"); await p.waitForTimeout(3500);
 await p.locator("input").first().fill("admin");await p.locator("input[type=password]").fill("123456");
 await p.locator("button",{hasText:"登录"}).first().click();
 await wf(async()=>!p.url().includes("/login"),15000,"in"); await p.waitForTimeout(600);
 await p.locator(".ls-side-item",{hasText:"验证"}).first().click();
 await wf(async()=>p.url().includes("/validation"),10000,"v"); await p.waitForTimeout(1200);
 console.log("active nav:", (await p.locator(".ls-side-item.active").first().textContent().catch(()=>''))?.trim());
 const shadows=await p.locator(".ls-side-item.active").first().evaluate(el=>getComputedStyle(el).boxShadow).catch(()=>'');
 console.log("active shadow:", shadows);
 const h=await p.evaluate(()=>{ const head=document.querySelector(".ls-val-head"); const tb=document.querySelector(".ls-val-toolbar"); return { head: head?head.clientHeight:0, tb: tb?tb.clientHeight:0 }; });
 console.log("head/toolbar:", h.head, "/", h.tb, "equal:", Math.abs(h.head-h.tb)<3);
 console.log("img figures:", await p.locator(".ls-val-img").count());
}catch(e){console.log("ERR "+e.message);}finally{await b.close();}})();
