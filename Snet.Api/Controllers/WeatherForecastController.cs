using Microsoft.AspNetCore.Mvc;
using Snet.Identify;
using Snet.Identify.handler;
using Snet.Identify.models.data;
using Snet.Identify.models.@enum;

namespace Snet.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class WeatherForecastController : ControllerBase
    {

        [HttpPost]
        public async Task<IActionResult> Post()
        {
            string pose = Path.Combine("files", "s-pose.onnx");
            IdentityOperate operatePoseEstimation = IdentityOperate.Instance(new IdentityData
            {
                OnnxPath = pose,
                //Hardware = new CpuExecutionProvider(),
                IdentifyType = IdentifyType.PoseEstimation
            });
            var result = await operatePoseEstimation.RunAsync(new PoseEstimationData(System.IO.File.ReadAllBytes(Path.Combine("files", "вкл╛.jpg"))));


            return Ok(result.GetPoseEstimationResult().Count);
        }
    }
}
