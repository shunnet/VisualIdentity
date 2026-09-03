
const {chromium}=require("playwright");
const baseUrl="http://127.0.0.1:5206";
async function wf(fn,t,l){const s=Date.now();while(Date.now()-s<t){try{const v=await fn();if(v)return v;}catch(e){}await new Promise(r=>setTimeout(r,250));}throw new Error("timeout "+l);}
(async()=>{const b=await chromium.launch({headless:true,channel:"msedge"});const p=await b.newPage({viewport:{width:390,height:844}});p.on("pageerror",e=>console.log("PAGEERROR: "+e.message));
try{
 await p.goto(baseUrl+"/login",{waitUntil:"domcontentloaded",timeout:30000}); await p.waitForTimeout(3800);
 await p.locator("input").first().fill("admin");await p.locator("input[type=password]").fill("123456");
 await p.locator("button",{hasText:"登录"}).first().click();
 await wf(async()=>!p.url().includes("/login"),15000,"in"); await p.waitForTimeout(900);
 await p.goto(baseUrl+"/projects",{waitUntil:"domcontentloaded"}); await p.waitForTimeout(2000);
 await p.screenshot({path:"F:/Snet/VisualIdentity/artifacts/review/A-m02-projects.png"});
 console.log("sidebar visible? width:", await p.locator(".ls-sidebar").evaluate(el=>getComputedStyle(el).width));
 console.log("hamburger visible:", await p.locator(".ls-nav-btn").isVisible());
 await p.locator(".ls-nav-btn").first().click(); await p.waitForTimeout(600);
 await p.screenshot({path:"F:/Snet/VisualIdentity/artifacts/review/A-m02-drawer.png"});
 console.log("drawer open x:", await p.locator(".ls-sidebar").evaluate(el=>getComputedStyle(el).transform));
 await p.locator(".ls-side-backdrop").first().click(); await p.waitForTimeout(500);
 console.log("backdrop closed:", await p.locator(".ls-side-backdrop").count());
 await p.goto(baseUrl+"/project/de01917b58164dfabfb9cbc0677df8e0",{waitUntil:"domcontentloaded"}); await p.waitForTimeout(2500);
 await p.screenshot({path:"F:/Snet/VisualIdentity/artifacts/review/A-m03-classify.png"});
 await p.goto(baseUrl+"/train/de01917b58164dfabfb9cbc0677df8e0",{waitUntil:"domcontentloaded"}); await p.waitForTimeout(2200);
 await p.screenshot({path:"F:/Snet/VisualIdentity/artifacts/review/A-m06-train.png"});
 await p.goto(baseUrl+"/validation",{waitUntil:"domcontentloaded"}); await p.waitForTimeout(2200);
 await p.screenshot({path:"F:/Snet/VisualIdentity/artifacts/review/A-m04-val.png"});
 await p.goto(baseUrl+"/users",{waitUntil:"domcontentloaded"}); await p.waitForTimeout(1800);
 await p.screenshot({path:"F:/Snet/VisualIdentity/artifacts/review/A-m05-users.png"});
 console.log("DONE A mobile");
}catch(e){console.log("ERR "+e.message.slice(0,400));}finally{await b.close();}})();