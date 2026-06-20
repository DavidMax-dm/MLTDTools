using System.Collections.Generic;
using System;
using Imas.Data.Serialized;
using JetBrains.Annotations;
using OpenMLTD.MillionDance.Entities.Internal;
using OpenMLTD.MillionDance.Entities.Vmd;
using OpenMLTD.MillionDance.Extensions;
using OpenMLTD.MillionDance.Utilities;
using OpenTK;

namespace OpenMLTD.MillionDance.Core {
    partial class VmdCreator {

        [NotNull]
        public VmdMotion CreateCameraMotion([CanBeNull] CharacterImasMotionAsset mainCamera, [NotNull] ScenarioObject baseScenario, [CanBeNull] CharacterImasMotionAsset cameraAppeal, AppealType appealType) {
            VmdCameraFrame[] frames;

            if (ProcessCameraFrames && mainCamera != null) {
                frames = CreateCameraFrames(mainCamera, baseScenario, cameraAppeal, appealType);
            } else {
                frames = null;
            }

            return new VmdMotion(CameraName, null, null, frames, null, null);
        }

        [NotNull, ItemNotNull]
        private VmdCameraFrame[] CreateCameraFrames([CanBeNull] CharacterImasMotionAsset mainCamera, [NotNull] ScenarioObject baseScenario, [CanBeNull] CharacterImasMotionAsset cameraAppeal, AppealType appealType) {
            // Here we reuse the logic in MVD camera frame computation
            var mvdCreator = new MvdCreator(_conversionConfig, _scalingConfig) {
                ProcessCameraFrames = true,
                ProcessBoneFrames = false,
                ProcessFacialFrames = false,
                ProcessLightFrames = false,
            };

            var mvdMotion = mvdCreator.CreateCameraMotion(mainCamera, baseScenario, cameraAppeal, appealType);
            var mvdFrames = mvdMotion.CameraMotions[0].CameraFrames;

            var cameraFrameList = new List<VmdCameraFrame>();

            foreach (var mvdFrame in mvdFrames) {
                var vmdFrame = new VmdCameraFrame((int)mvdFrame.FrameNumber);

                vmdFrame.Length = mvdFrame.Distance * 0.1f;
                vmdFrame.Position = mvdFrame.Position;
                vmdFrame.Orientation = mvdFrame.Rotation + new Vector3(MathHelper.Pi, 0, MathHelper.Pi);
                vmdFrame.IsCut = mvdFrame.IsCut;

                // VMD stores FOV as an unsigned integer in degrees. The MVD frame
                // already contains the source camera's FOV in radians, so preserve
                // it per frame rather than replacing it with a fixed value.
                var fov = MathHelper.RadiansToDegrees(mvdFrame.FieldOfView);
                vmdFrame.FieldOfView = (uint)Math.Round(fov, MidpointRounding.AwayFromZero);

                cameraFrameList.Add(vmdFrame);
            }

            UnwrapCameraOrientations(cameraFrameList);
            return ReduceCameraKeyframes(cameraFrameList);
        }

        [NotNull, ItemNotNull]
        private static VmdCameraFrame[] ReduceCameraKeyframes([NotNull] List<VmdCameraFrame> frames) {
            if (frames.Count <= 2) {
                SetLinearInterpolation(frames);
                return frames.ToArray();
            }

            // VMD has one shared timeline for position, rotation, distance and FOV.
            // Keep a frame whenever linearly interpolating any of those channels would
            // visibly deviate from the sampled MLTD camera motion.
            var keep = new bool[frames.Count];
            var anchors = new List<int> { 0 };

            for (var i = 1; i < frames.Count; ++i) {
                if (!frames[i].IsCut) {
                    continue;
                }

                // Keep the final frame before the cut and the first frame after it.
                // The one-frame segment is the VMD equivalent of Unity's constant
                // tangent at a camera-cut boundary.
                anchors.Add(i - 1);
                anchors.Add(i);
            }

            anchors.Add(frames.Count - 1);
            anchors.Sort();

            foreach (var anchor in anchors) {
                keep[anchor] = true;
            }

            var pendingRanges = new Stack<KeyValuePair<int, int>>();

            for (var i = 1; i < anchors.Count; ++i) {
                var start = anchors[i - 1];
                var end = anchors[i];

                if (start != end) {
                    pendingRanges.Push(new KeyValuePair<int, int>(start, end));
                }
            }

            while (pendingRanges.Count > 0) {
                var range = pendingRanges.Pop();
                var splitIndex = FindLargestInterpolationError(frames, range.Key, range.Value);

                if (splitIndex < 0) {
                    continue;
                }

                keep[splitIndex] = true;
                pendingRanges.Push(new KeyValuePair<int, int>(range.Key, splitIndex));
                pendingRanges.Push(new KeyValuePair<int, int>(splitIndex, range.Value));
            }

            var result = new List<VmdCameraFrame>();

            for (var i = 0; i < frames.Count; ++i) {
                if (keep[i]) {
                    result.Add(frames[i]);
                }
            }

            SetLinearInterpolation(result);
            return result.ToArray();
        }

        private static int FindLargestInterpolationError([NotNull] List<VmdCameraFrame> frames, int startIndex, int endIndex) {
            if (endIndex - startIndex <= 1) {
                return -1;
            }

            var start = frames[startIndex];
            var end = frames[endIndex];
            var frameCount = end.FrameIndex - start.FrameIndex;

            if (frameCount <= 0) {
                return -1;
            }

            var largestError = 1.0f;
            var largestErrorIndex = -1;

            for (var i = startIndex + 1; i < endIndex; ++i) {
                var frame = frames[i];
                var t = (frame.FrameIndex - start.FrameIndex) / (float)frameCount;
                var error = GetNormalizedInterpolationError(start, end, frame, t);

                if (error > largestError) {
                    largestError = error;
                    largestErrorIndex = i;
                }
            }

            return largestErrorIndex;
        }

        private static float GetNormalizedInterpolationError([NotNull] VmdCameraFrame start, [NotNull] VmdCameraFrame end, [NotNull] VmdCameraFrame actual, float t) {
            var expectedPosition = Vector3.Lerp(start.Position, end.Position, t);
            var positionError = MaxComponentDistance(actual.Position, expectedPosition) / PositionErrorTolerance;

            var expectedOrientation = Vector3.Lerp(start.Orientation, end.Orientation, t);
            var orientationError = MaxComponentDistance(actual.Orientation, expectedOrientation) / OrientationErrorTolerance;

            var expectedLength = start.Length + (end.Length - start.Length) * t;
            var lengthError = Math.Abs(actual.Length - expectedLength) / LengthErrorTolerance;

            var expectedFov = start.FieldOfView + ((float)end.FieldOfView - start.FieldOfView) * t;
            var fovError = Math.Abs(actual.FieldOfView - expectedFov) / FovErrorTolerance;

            return Math.Max(Math.Max(positionError, orientationError), Math.Max(lengthError, fovError));
        }

        private static void SetLinearInterpolation([NotNull, ItemNotNull] List<VmdCameraFrame> frames) {
            foreach (var frame in frames) {
                for (var channel = 0; channel < 6; ++channel) {
                    frame.Interpolation[channel, 0] = 20;
                    frame.Interpolation[channel, 1] = 107;
                    frame.Interpolation[channel, 2] = 20;
                    frame.Interpolation[channel, 3] = 107;
                }
            }
        }

        private static void UnwrapCameraOrientations([NotNull, ItemNotNull] List<VmdCameraFrame> frames) {
            for (var i = 1; i < frames.Count; ++i) {
                var previous = frames[i - 1];
                var current = frames[i];

                if (current.IsCut) {
                    continue;
                }

                current.Orientation = new Vector3(
                    UnwrapAngle(previous.Orientation.X, current.Orientation.X),
                    UnwrapAngle(previous.Orientation.Y, current.Orientation.Y),
                    UnwrapAngle(previous.Orientation.Z, current.Orientation.Z));
            }
        }

        private static float UnwrapAngle(float previous, float current) {
            var delta = current - previous;
            var twoPi = MathHelper.Pi * 2;

            while (delta > MathHelper.Pi) {
                delta -= twoPi;
            }

            while (delta < -MathHelper.Pi) {
                delta += twoPi;
            }

            return previous + delta;
        }

        private static float MaxComponentDistance(Vector3 a, Vector3 b) {
            return Math.Max(Math.Abs(a.X - b.X), Math.Max(Math.Abs(a.Y - b.Y), Math.Abs(a.Z - b.Z)));
        }

        // Values are in VMD coordinates, radians, and degrees respectively. They are
        // deliberately conservative so the curve is compressed only when the visual
        // result stays close to the full-frame source evaluation.
        private const float PositionErrorTolerance = 0.1f;
        private const float LengthErrorTolerance = 0.1f;
        private const float OrientationErrorTolerance = MathHelper.Pi / 720; // 0.25 degrees
        // VMD stores FOV as an integer. A sub-degree source change can therefore
        // appear as a one-frame step after rounding; allowing up to two degrees of
        // fitting error prevents those rounding boundaries from becoming keyframes.
        private const float FovErrorTolerance = 2.0f;

    }
}
