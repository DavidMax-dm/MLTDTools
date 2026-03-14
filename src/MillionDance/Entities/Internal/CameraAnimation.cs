using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Imas.Data.Serialized;
using JetBrains.Annotations;
using OpenMLTD.MillionDance.Core;
using OpenMLTD.MillionDance.Entities.Extensions;
using OpenMLTD.MillionDance.Utilities;

namespace OpenMLTD.MillionDance.Entities.Internal {
    public sealed class CameraAnimation {

        private CameraAnimation([NotNull, ItemNotNull] CameraFrame[] frames, float duration, int frameCount) {
            CameraFrames = frames;
            Duration = duration;
            FrameCount = frameCount;
        }

        [NotNull, ItemNotNull]
        public CameraFrame[] CameraFrames { get; }

        public float Duration { get; }

        public int FrameCount { get; }

        [NotNull]
        public static CameraAnimation CreateFrom([NotNull] CharacterImasMotionAsset cameraMotion) {
            var focalLengthCurve = cameraMotion.Curves.FirstOrDefault(curve => curve.Path == "CamBase" && curve.GetPropertyName() == "focalLength");
            var camCutCurve = cameraMotion.Curves.FirstOrDefault(curve => curve.Path == "CamBase" && curve.GetPropertyName() == "camCut");
            var angleXCurve = cameraMotion.Curves.FirstOrDefault(curve => curve.Path == "CamBaseS" && curve.GetPropertyType() == PropertyType.AngleX);
            var angleYCurve = cameraMotion.Curves.FirstOrDefault(curve => curve.Path == "CamBaseS" && curve.GetPropertyType() == PropertyType.AngleY);
            var angleZCurve = cameraMotion.Curves.FirstOrDefault(curve => curve.Path == "CamBaseS" && curve.GetPropertyType() == PropertyType.AngleZ);
            var posXCurve = cameraMotion.Curves.FirstOrDefault(curve => curve.Path == "CamBaseS" && curve.GetPropertyType() == PropertyType.PositionX);
            var posYCurve = cameraMotion.Curves.FirstOrDefault(curve => curve.Path == "CamBaseS" && curve.GetPropertyType() == PropertyType.PositionY);
            var posZCurve = cameraMotion.Curves.FirstOrDefault(curve => curve.Path == "CamBaseS" && curve.GetPropertyType() == PropertyType.PositionZ);
            var targetXCurve = cameraMotion.Curves.FirstOrDefault(curve => curve.Path == "CamTgtS" && curve.GetPropertyType() == PropertyType.PositionX);
            var targetYCurve = cameraMotion.Curves.FirstOrDefault(curve => curve.Path == "CamTgtS" && curve.GetPropertyType() == PropertyType.PositionY);
            var targetZCurve = cameraMotion.Curves.FirstOrDefault(curve => curve.Path == "CamTgtS" && curve.GetPropertyType() == PropertyType.PositionZ);

            var allCameraCurves = new[] {
                focalLengthCurve, camCutCurve, angleXCurve, angleYCurve, angleZCurve, posXCurve, posYCurve, posZCurve, targetXCurve, targetYCurve, targetZCurve
            };

            if (AnyoneIsNull(allCameraCurves)) {
                throw new ApplicationException("Invalid camera motion file.");
            }

            Debug.Assert(focalLengthCurve != null, nameof(focalLengthCurve) + " != null");
            Debug.Assert(camCutCurve != null, nameof(camCutCurve) + " != null");
            Debug.Assert(angleXCurve != null, nameof(angleXCurve) + " != null");
            Debug.Assert(angleYCurve != null, nameof(angleYCurve) + " != null");
            Debug.Assert(angleZCurve != null, nameof(angleZCurve) + " != null");
            Debug.Assert(posXCurve != null, nameof(posXCurve) + " != null");
            Debug.Assert(posYCurve != null, nameof(posYCurve) + " != null");
            Debug.Assert(posZCurve != null, nameof(posZCurve) + " != null");
            Debug.Assert(targetXCurve != null, nameof(targetXCurve) + " != null");
            Debug.Assert(targetYCurve != null, nameof(targetYCurve) + " != null");
            Debug.Assert(targetZCurve != null, nameof(targetZCurve) + " != null");

            if (!AllUseFCurve(allCameraCurves)) {
                throw new ApplicationException("Invalid key type.");
            }

            // 定义基本参数
            const float frameRate = 60.0f; // 强制 60 FPS
            const float frameDuration = 1.0f / frameRate;
            float scale = 2.0f; // 缩放倍率：2.0 代表动画时长翻倍（变慢）

            // 计算时长和帧数
            var originalDuration = GetMaxDuration(allCameraCurves);
            var totalDuration = originalDuration * scale; 
            var frameCount = (int)Math.Round(totalDuration / frameDuration);

            // 初始化数组
            var cameraFrames = new CameraFrame[frameCount];

            // 开始采样
            for (var i = 0; i < frameCount; ++i) {
                var frame = new CameraFrame();
                
                // exportTime 是导出文件里的时间戳 (0.0, 0.016, 0.033...)
                var exportTime = i * frameDuration;

                // rawTime 是映射回原始数据的时间 (如果是 2倍缩放，导出到 2s 时采样原始数据的 1s)
                var rawTime = exportTime / scale;

                // 采样时间偏移：在原始时间轴上微偏，避开关键帧边界
                var sampleTime = rawTime + (0.1f * (frameDuration / scale));

                // 赋值给导出帧
                frame.Time = exportTime;
                
                // 核心采样逻辑：全部使用映射回来的 sampleTime
                frame.FocalLength = GetInterpolatedValue(focalLengthCurve, sampleTime);
                frame.Cut = (int)GetLowerClampedValue(camCutCurve, sampleTime);
                frame.AngleX = GetInterpolatedValue(angleXCurve, sampleTime);
                frame.AngleY = GetInterpolatedValue(angleYCurve, sampleTime);
                frame.AngleZ = GetInterpolatedValue(angleZCurve, sampleTime);
                frame.PositionX = GetInterpolatedValue(posXCurve, sampleTime);
                frame.PositionY = GetInterpolatedValue(posYCurve, sampleTime);
                frame.PositionZ = GetInterpolatedValue(posZCurve, sampleTime);
                frame.TargetX = GetInterpolatedValue(targetXCurve, sampleTime);
                frame.TargetY = GetInterpolatedValue(targetYCurve, sampleTime);
                frame.TargetZ = GetInterpolatedValue(targetZCurve, sampleTime);

                cameraFrames[i] = frame;
            }

            return new CameraAnimation(cameraFrames, totalDuration, frameCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AnyoneIsNull<T>([NotNull, ItemCanBeNull] params T[] objects) {
            return objects.Any(x => ReferenceEquals(x, null));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AllUseFCurve([NotNull, ItemNotNull] params Curve[] curves) {
            return curves.All(curve => curve.GetKeyType() == KeyType.FCurve);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float GetMaxDuration([NotNull, ItemNotNull] params Curve[] curves) {
            float duration = 0;

            foreach (var curve in curves) {
                Debug.Assert(curve.Values.Length % 4 == 0);

                var frameCount = curve.Values.Length / 4;

                for (var i = 0; i < frameCount; ++i) {
                    var time = curve.Values[i * 4];

                    if (time > duration) {
                        duration = time;
                    }
                }
            }

            return duration;
        }

        private static float GetInterpolatedValue([NotNull] Curve curve, float time) {
            var valueCount = curve.Values.Length;

            Debug.Assert(valueCount % 4 == 0);

            valueCount = valueCount / 4;

            for (var i = 0; i < valueCount; ++i) {
                if (i < valueCount - 1) {
                    var nextTime = curve.Values[(i + 1) * 4];

                    if (time > nextTime) {
                        continue;
                    }

                    var curTime = curve.Values[i * 4];
                    var curValue = curve.Values[i * 4 + 1];
                    var nextValue = curve.Values[(i + 1) * 4 + 1];
                    var tan1 = curve.Values[i * 4 + 3];
                    var tan2 = curve.Values[(i + 1) * 4 + 2];

                    // suspect:
                    // +2: tan(in)
                    // +3: tan(out)

                    var dt = nextTime - curTime;
                    var t = (time - curTime) / dt;

                    // TODO: use F-curve interpolation.
                    //return Lerp(curValue, nextValue, t);
                    return ComputeFCurveNaive(curValue, nextValue, tan1, tan2, dt, t);
                } else {
                    return curve.Values[i * 4 + 1];
                }
            }

            throw new ArgumentException("Maybe time is invalid.");
        }

        private static float GetLowerClampedValue([NotNull] Curve curve, float time) {
            var valueCount = curve.Values.Length;

            Debug.Assert(valueCount % 4 == 0);

            valueCount = valueCount / 4;

            for (var i = 0; i < valueCount; ++i) {
                if (i < valueCount - 1) {
                    var nextTime = curve.Values[(i + 1) * 4];

                    if (time > nextTime) {
                        continue;
                    }

                    var curValue = curve.Values[i * 4 + 1];

                    return curValue;
                } else {
                    return curve.Values[i * 4 + 1];
                }
            }

            throw new ArgumentException("Maybe time is invalid.");
        }

        // https://developer.blender.org/diffusion/B/browse/master/source/blender/blenkernel/intern/fcurve.c;e50a3dd4c4e9a9898df31e444d1002770b4efb9c$2212

        // Key: some mathematics
        // See http://luthuli.cs.uiuc.edu/~daf/courses/cs-419/Week-12/Interpolation-2013.pdf pg. 26
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeFCurveNaive(float value1, float value2, float tan1, float tan2, float dt, float t) {
            bool isInf1 = float.IsInfinity(tan1), isInf2 = float.IsInfinity(tan2);

            //float factor = Math.Abs(value2 - value1);
            //float factor = value2 - value1;
            //float factor = dt;
            //float factor = 1;
            //float factor = 2; // funny effect when playing Blooming Star at "きらめきに憧れて　胸で"
            //float factor = 3; // too large

            // There should be another parameter to specify the exact location tuple (time, value) is,
            // for example handle length. From differentiation we know the tangents of the control
            // points. So here variable "factor" means a mutiplication factor, which is similar to
            // handle length. We use this to compute the location of one or two control points.
            // For F-curve editing (and why I guess like above), see Blender's manual:
            // https://docs.blender.org/manual/en/dev/editors/graph_editor/fcurves/introduction.html
            float factor = dt;

            if (isInf1) {
                if (isInf2) {
                    return GeometryHelper.Lerp(value1, value2, t);
                } else {
                    var cp = value2 - tan2 / 3 * factor;
                    return Bezier(value1, cp, value2, t);
                }
            } else {
                if (isInf2) {
                    var cp = value1 + tan1 / 3 * factor;
                    return Bezier(value1, cp, value2, t);
                } else {
                    var cp1 = value1 + tan1 / 3 * factor;
                    var cp2 = value2 - tan2 / 3 * factor;
                    return Bezier(value1, cp1, cp2, value2, t);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Bezier(float p1, float cp, float p2, float t) {
            return (1 - t) * (1 - t) * p1 + 2 * t * (1 - t) * cp + t * t * p2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Bezier(float p1, float cp1, float cp2, float p2, float t) {
            var tt = 1 - t;
            var tt2 = tt * tt;
            var t2 = t * t;

            return tt * tt2 * p1 + 3 * tt2 * t * cp1 + 3 * tt * t2 * cp2 + t * t2 * p2;
        }

    }
}
