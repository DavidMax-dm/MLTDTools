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

                // VMD stores FOV as an unsigned integer in degrees. The MVD frame
                // already contains the source camera's FOV in radians, so preserve
                // it per frame rather than replacing it with a fixed value.
                var fov = MathHelper.RadiansToDegrees(mvdFrame.FieldOfView);
                vmdFrame.FieldOfView = (uint)Math.Round(fov, MidpointRounding.AwayFromZero);

                cameraFrameList.Add(vmdFrame);
            }

            return cameraFrameList.ToArray();
        }

    }
}
