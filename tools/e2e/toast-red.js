
const {chromium}=require("playwright");
const baseUrl="http://127.0.0.1:5206";
async function wf(fn,t,l){const s=Date.now();while(Date.now()-s<t){try{const v=await fn();if(v)return v;}catch(e){}await new Promise(r=>setTimeout(r,250));}throw new Error("timeout "+l);}
(async()=>{const b=await chromium.launch({headless:true,channel:"msedge"});const p=await b.newPage({viewport:{width:1500,height:850}});
p.on("pageerror",e=>console.log("PAGEERROR: "+e.message));
try{
 await p.goto(baseUrl+"/login",{waitUntil:"domcontentloaded",timeout:30000}); await p.waitForTimeout(4000);
 await p.locator("input").first().fill("admin");await p.locator("input[type=password]").fill("123456");
 await p.locator("button",{hasText:"登录"}).first().click();
 await wf(async()=>!p.url().includes("/login"),15000,"in"); await p.waitForTimeout(800);
 await p.locator("button",{hasText:"新建图像工程"}).first().click();
 await p.waitForSelector(".modal",{state:"attached",timeout:8000});
 await p.locator(".modal select").selectOption("classify").catch(()=>{});
 await p.locator(".modal input").first().fill("toast-"+Date.now());
 await p.locator(".modal button",{hasText:"创建"}).first().click();
 await wf(async()=>p.url().includes("/project/"),20000,"proj"); await p.waitForTimeout(1500);
 // 1st import: class car
 await p.locator("button",{hasText:"导入图片"}).first().click();
 await wf(async()=>(await p.locator(".modal input[type=file]").count())>=1,8000,"imp");
 await p.locator(".modal input").nth(0).fill("car");
 await p.locator(".modal input").nth(0).press("Tab");
 await p.waitForTimeout(400);
 const fi=p.locator(".modal input[type=file]").first();
 await fi.setInputFiles(["F:/Snet/VisualIdentity/Snet.Yolo.Tasks/wwwroot/samples/demo.jpg"]);
 await p.waitForTimeout(1500);
 console.log("G file count text:", await p.locator(".modal .small").first().textContent().catch(e=>"none"));
 const btn=p.locator(".modal button",{hasText:"创建"}).first();
 console.log("H create disabled:", await btn.isDisabled());
 await btn.click({timeout:8000});
 await wf(async()=>(await p.locator(".ls-folder-card").count())>=1,20000,"folder1");
 console.log("I folders:", await p.locator(".ls-folder-card").count());
 await p.waitForTimeout(4000);
 // 2nd import same class
 await p.locator("button",{hasText:"导入图片"}).first().click();
 await wf(async()=>(await p.locator(".modal input[type=file]").count())>=1,8000,"imp2");
 await p.locator(".modal input").nth(0).fill("car");
 await p.locator(".modal input").nth(0).press("Tab");
 await p.waitForTimeout(400);
 await p.locator(".modal input[type=file]").first().setInputFiles(["C:/Users/vipls/Desktop/models/e.bmp"]);
 await p.waitForTimeout(1500);
 await p.locator(".modal button",{hasText:"创建"}).first().click({timeout:8000});
 await wf(async()=>(await p.locator(".ls-toast-error").count())>=1,10000,"redtoast");
 const t=p.locator(".ls-toast-error").first();
 console.log("RED toast msg:", (await t.textContent()).trim());
 console.log("RED border-left-color:", await t.evaluate(el=>getComputedStyle(el).borderLeftColor));
 console.log("RED cls:", await t.getAttribute("class"));
 console.log("folders still:", await p.locator(".ls-folder-card").count());
 await p.screenshot({path:"F:/Snet/VisualIdentity/artifacts/toast-red.png"});
}catch(e){console.log("ERR "+e.message.slice(0,500));}finally{await b.close();}})();