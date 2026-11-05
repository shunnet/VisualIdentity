using System.Text.Json.Serialization;
using Snet.Yolo.Server.yolo.Models;

namespace Snet.Yolo.Server.handler
{
    /// <summary>
    /// 姿态估计自定义关键点颜色处理程序
    /// </summary>
    public class PoseEstimationCustomKeyPointColorHandler
    {
        /// <summary>
        /// 关键点类型（KeyPointType）
        /// </summary>
        /// <remarks>
        /// 此枚举定义了常见的人体解剖关键点类型，必须与训练模型输出中的关键点顺序保持完全一致，<br/>
        /// 否则在推理或渲染过程中会出现关键点错位的问题。<br/>
        /// <br/>
        /// 通常用于人体姿态估计（Pose Estimation）或动作识别中，枚举值按照人体结构从上到下、由中线到两侧的顺序排列。<br/>
        /// <br/>
        /// 示例：<br/>
        /// - Nose（鼻子）作为人体中心参考点<br/>
        /// - Eyes / Ears（眼睛 / 耳朵）常用于头部方向估计<br/>
        /// - Shoulders / Elbows / Wrists（肩、肘、腕）用于上肢姿态跟踪<br/>
        /// - Hips / Knees / Ankles（髋、膝、踝）用于下肢姿态与运动分析
        /// </remarks>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum KeyPointType
        {
            /// <summary>鼻子</summary>
            Nose,

            /// <summary>左眼</summary>
            LeftEye,

            /// <summary>右眼</summary>
            RightEye,

            /// <summary>左耳</summary>
            LeftEar,

            /// <summary>右耳</summary>
            RightEar,

            /// <summary>左肩</summary>
            LeftShoulder,

            /// <summary>右肩</summary>
            RightShoulder,

            /// <summary>左肘</summary>
            LeftElbow,

            /// <summary>右肘</summary>
            RightElbow,

            /// <summary>左手腕</summary>
            LeftWrist,

            /// <summary>右手腕</summary>
            RightWrist,

            /// <summary>左髋部</summary>
            LeftHip,

            /// <summary>右髋部</summary>
            RightHip,

            /// <summary>左膝</summary>
            LeftKnee,

            /// <summary>右膝</summary>
            RightKnee,

            /// <summary>左脚踝</summary>
            LeftAnkle,

            /// <summary>右脚踝</summary>
            RightAnkle
        }

        /// <summary>
        /// 关键点颜色类型（KeyPointColor）
        /// </summary>
        /// <remarks>
        /// 此枚举用于标识在可视化时关键点的显示颜色，颜色值通常以十六进制形式表示，<br/>
        /// 例如 "Green" 对应 "#00FF00"。<br/>
        /// <br/>
        /// 这些颜色可用于：<br/>
        /// - 区分不同类型的关键点（如头部、上肢、下肢）<br/>
        /// - 增强人体姿态渲染的可读性<br/>
        /// - 便于在调试或展示时快速识别关键点分组<br/>
        /// </remarks>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum KeyPointColor
        {
            /// <summary>绿色 (#00FF00)，常用于头部关键点</summary>
            Green,

            /// <summary>浅蓝色 (#ADD8E6)，常用于上肢关键点</summary>
            LightBlue,

            /// <summary>黄色 (#FFFF00)，常用于躯干关键点</summary>
            Yellow,

            /// <summary>亮粉色 (#FF69B4)，常用于下肢关键点</summary>
            HotPink
        }


        /// <summary>
        /// 返回一组代表解剖学标志的关键点标记，每种关键点类型都可以有可选的自定义颜色。
        /// </summary>
        /// <remarks>
        /// 返回的数组包含常见解剖学关键点的标记，例如鼻子、眼睛、耳朵、肩膀、肘部、手腕、髋部、膝盖和脚踝。  
        /// 如果未提供 colors 参数，将为每种关键点类型应用一组预定义的颜色。  
        /// </remarks>
        /// <param name="colors">
        /// 一个可选的字典，用于将每个关键点类型映射到其颜色值的字符串表示。  
        /// 如果为 null，则所有关键点都会使用默认颜色。  
        /// </param>
        /// <returns>
        /// 一个 KeyPointMarker 对象数组，每个对象描述一个关键点及其相关的颜色和连接信息。  
        /// </returns>
        public KeyPointMarker[] GetKeyPoints(Dictionary<KeyPointColor, string>? colors = null)
        {
            colors ??= new()
            {
                { KeyPointColor.Green, "#A2FF33" },     // Light green
                { KeyPointColor.LightBlue, "#33ACFF" }, // Light blue
                { KeyPointColor.Yellow, "#FFF633" },    // Yellow
                { KeyPointColor.HotPink, "#FF33AC" }    // Hot pink
            };

            return [new () // Nose
                    {
                        Color = colors[KeyPointColor.Green],
                        Connections =
                        [
                            new ((int)KeyPointType.LeftEye, colors[KeyPointColor.Green]),
                            new ((int)KeyPointType.RightEye, colors[KeyPointColor.Green])
                        ]
                    },
                    new () // Left eye
                    {
                        Color = colors[KeyPointColor.Green],
                        Connections = [ new ((int)KeyPointType.RightEye, colors[KeyPointColor.Green]) ]
                    },
                    new () // Right eye
                    {
                        Color = colors[KeyPointColor.Green],
                    },
                    new () // Left ear
                    {
                        Color = colors[KeyPointColor.Green],
                        Connections =
                        [
                            new ((int)KeyPointType.LeftEye, colors[KeyPointColor.Green]),
                            new ((int)KeyPointType.LeftShoulder, colors[KeyPointColor.Green]),
                        ]
                    },
                    new () // Right ear
                    {
                        Color = colors[KeyPointColor.Green],
                        Connections =
                        [
                            new ((int)KeyPointType.RightEye, colors[KeyPointColor.Green]),
                            new ((int)KeyPointType.RightShoulder, colors[KeyPointColor.Green]),
                        ]
                    },
                    new () // Left shoulder
                    {
                        Color = colors[KeyPointColor.LightBlue],
                        Connections =
                        [
                            new ((int)KeyPointType.RightShoulder, colors[KeyPointColor.LightBlue]),
                            new ((int)KeyPointType.LeftElbow, colors[KeyPointColor.LightBlue]),
                            new ((int)KeyPointType.LeftHip, colors[KeyPointColor.HotPink])
                        ]
                    },
                    new () // Right shoulder
                    {
                        Color = colors[KeyPointColor.LightBlue],
                        Connections =
                        [
                            new ((int)KeyPointType.RightElbow, colors[KeyPointColor.LightBlue]),
                            new ((int)KeyPointType.RightHip, colors[KeyPointColor.HotPink])
                        ]
                    },
                    new () // Left elbow
                    {
                        Color = colors[KeyPointColor.LightBlue],
                        Connections = [ new ((int)KeyPointType.LeftWrist, colors[KeyPointColor.LightBlue]) ]
                    },
                    new () // Right elbow
                    {
                        Color = colors[KeyPointColor.LightBlue],
                        Connections = [ new ((int)KeyPointType.RightWrist, colors[KeyPointColor.LightBlue]) ]
                    },
                    new () // Left wrist
                    {
                        Color = colors[KeyPointColor.LightBlue]
                    },
                    new () // Right wrist
                    {
                        Color = colors[KeyPointColor.LightBlue]
                    },
                    new () // Left hip
                    {
                        Color = colors[KeyPointColor.Yellow],
                        Connections =
                        [
                            new ((int)KeyPointType.RightHip, colors[KeyPointColor.HotPink]),
                            new ((int)KeyPointType.LeftKnee, colors[KeyPointColor.Yellow])
                        ]
                    },
                    new () // Right hip
                    {
                        Color = colors[KeyPointColor.Yellow],
                        Connections = [ new ((int)KeyPointType.RightKnee, colors[KeyPointColor.Yellow]) ]
                    },
                    new () // Left knee
                    {
                        Color = colors[KeyPointColor.Yellow],
                        Connections = [ new ((int)KeyPointType.LeftAnkle, colors[KeyPointColor.Yellow]) ]
                    },
                    new () // Right knee
                    {
                        Color = colors[KeyPointColor.Yellow],
                        Connections = [ new ((int)KeyPointType.RightAnkle, colors[KeyPointColor.Yellow]) ]
                    },
                    new () // Left ankle
                    {
                        Color = colors[KeyPointColor.Yellow]
                    },
                    new () // Right ankle
                    {
                        Color = colors[KeyPointColor.Yellow]
                    }
                ];
        }
    }
}
