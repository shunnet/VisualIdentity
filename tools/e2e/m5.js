
const {chromium}=require("playwright");
const baseUrl="http://127.0.0.1:5206";
async function wf(fn,t,l){const s=Date.now();while(Date.now()-s<t){try{const v=await fn();if(v)return v;}catch(e){}await new Promise(r=>setTimeout(r,300));}throw new Error("timeout "+l);}
(async()=>{const b=await chromium.launch({headless:true,channel:"msedge"});const p=await b.newPage({viewport:{width:1440,height:900}});p.on("pageerror",e=>console.log("[PAGEERROR] "+e.message));
try{
 await p.goto(baseUrl+"/",{waitUntil:"domcontentloaded",timeout:30000});
 await wf(async()=>p.url().includes("/login"),10000,"l"); await p.waitForTimeout(3500);
 await p.locator("input").first().fill("admin");await p.locator("input[type=password]").fill("123456");
 await p.locator("button",{hasText:"登录"}).first().click();
 await wf(async()=>!p.url().includes("/login"),15000,"in"); await p.waitForTimeout(800);
 await p.locator(".ls-side-item",{hasText:"项目"}).first().click().catch(()=>p.locator(".ls-side-item",{hasText:"Projects"}).first().click());
 await wf(async()=>p.url().includes("/projects"),10000,"p"); await p.waitForTimeout(600);
 // create project
 await p.locator("button",{hasText:"新建图像工程"}).first().click();
 await p.waitForSelector(".modal",{state:"attached",timeout:8000});
 await p.locator(".modal select").selectOption("ObjectDetection").catch(()=>p.locator(".modal select").selectOption({index:0}));
 await p.locator(".modal input.form-control").first().fill("M5Proj-"+Date.now());
 await p.locator(".modal button",{hasText:"创建"}).first().click().catch(()=>p.locator(".modal button",{hasText:"Create"}).first().click());
 await wf(async()=>p.url().includes("/project/"),15000,"proj");
 console.log("project created:", p.url());
 await p.screenshot({path:"F:/Snet/VisualIdentity/artifacts/m5proj.png"});
 console.log("project details ok");
}catch(e){console.log("ERR "+e.message);}finally{await b.close();}})();
