
const {chromium}=require("playwright");
const baseUrl="http://127.0.0.1:5206";
async function wf(fn,t,l){const s=Date.now();while(Date.now()-s<t){try{const v=await fn();if(v)return v;}catch(e){}await new Promise(r=>setTimeout(r,200));}throw new Error("timeout "+l);}
(async()=>{const b=await chromium.launch({headless:true,channel:"msedge"});const p=await b.newPage({viewport:{width:1500,height:850}});
try{
 await p.goto(baseUrl+"/login",{waitUntil:"domcontentloaded",timeout:30000}); await p.waitForTimeout(4000);
 await p.locator("input").first().fill("admin");await p.locator("input[type=password]").fill("123456");
 await p.locator("button",{hasText:"登录"}).first().click();
 await wf(async()=>!p.url().includes("/login"),15000,"in"); await p.waitForTimeout(600);
 // create a classify project
 await p.locator("button",{hasText:"新建图像工程"}).first().click();
 await p.waitForSelector(".modal",{state:"attached",timeout:8000});
 await p.locator(".modal select").selectOption("classify").catch(()=>{});
 await p.locator(".modal input.form-control").first().fill("C-"+Date.now());
 await p.locator(".modal button",{hasText:"创建"}).first().click();
 await wf(async()=>p.url().includes("/project/"),15000,"proj"); await p.waitForTimeout(1200);
 console.log("url:", p.url());
 console.log("folder grid:", await p.locator(".ls-folder-grid").count());
 // import class images (header import button)
 await p.locator("button",{hasText:"导入图片"}).first().click();
 await wf(async()=>(await p.locator(".modal .ls-field-label").count())>=1,8000,"imp");
 console.log("import modal:", await p.locator(".modal").count());
 await p.locator(".modal input.form-control").first().fill("car");
 await p.locator(".modal input[type=file]").setInputFiles(["F:/Snet/VisualIdentity/Snet.Yolo.Tasks/wwwroot/samples/demo.jpg","C:/Users/vipls/Desktop/models/e.bmp"]);
 await p.waitForTimeout(1200);
 console.log("files shown:", await p.locator(".modal .small").first().textContent().catch(()=>''));
 await p.locator(".modal button",{hasText:"创建"}).first().click();
 await wf(async()=>(await p.locator(".ls-folder-card").count())>=1,15000,"folder");
 console.log("folders:", await p.locator(".ls-folder-card").count());
 console.log("folder name:", await p.locator(".ls-folder-name").first().textContent());
 console.log("folder count:", await p.locator(".ls-folder-count").first().textContent());
}catch(e){console.log("ERR "+e.message);}finally{await b.close();}})();
