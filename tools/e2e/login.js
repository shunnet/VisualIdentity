
const path=require("path"),{chromium}=require("playwright");
const baseUrl="http://127.0.0.1:5204";
async function wf(fn,t,l){const s=Date.now();while(Date.now()-s<t){try{const v=await fn();if(v)return v;}catch(e){}await new Promise(r=>setTimeout(r,300));}throw new Error("timeout "+l);}
(async()=>{const b=await (async()=>{for(const a of [{n:"msedge",o:{channel:"msedge"}},{n:"chromium",o:{}}]){try{return await chromium.launch({headless:true,...a.o});}catch(e){}}throw new Error("no browser");})();
const p=await b.newPage({viewport:{width:1440,height:900}});p.on("pageerror",e=>console.log("PAGEERROR: "+e.message));
try{
 await p.goto(baseUrl+"/",{waitUntil:"domcontentloaded",timeout:30000});
 await wf(async()=>p.url().includes("/login"),10000,"login redirect");
 console.log("redirected to:", p.url());
 await p.locator("input").first().fill("admin");
 await p.locator("input[type=password]").fill("123456");
 await p.locator("button",{hasText:"登录"}).first().click().catch(()=>p.locator("button",{hasText:"Login"}).first().click());
 await wf(async()=>p.url().replace(/\/$/,"")==="" || p.url().endsWith("/"),15000,"home");
 console.log("after login:", p.url());
 await p.waitForTimeout(1800);
 console.log("sidebar:", await p.locator(".ls-sidebar").count(), "| nav items:", await p.locator(".ls-side-item").count());
 await p.screenshot({path:"F:/Snet/VisualIdentity/artifacts/home.png"});console.log("home shot");
 await p.locator(".ls-side-item",{hasText:"项目"}).first().click().catch(()=>p.locator(".ls-side-item",{hasText:"Projects"}).first().click());
 await wf(async()=>p.url().includes("/projects"),10000,"projects");
 await p.waitForTimeout(900);
 console.log("projects:", p.url(), "| new project btn:", await p.locator("button",{hasText:"新建图像工程"}).count());
 await p.screenshot({path:"F:/Snet/VisualIdentity/artifacts/projects.png"});console.log("projects shot");
 await p.locator(".ls-side-toggle").first().click(); await p.waitForTimeout(500);
 console.log("collapsed:", (await p.locator(".ls-sidebar").getAttribute("class")).includes("collapsed"));
}catch(err){console.log("ERR: "+err.message);}finally{await b.close();}})();
