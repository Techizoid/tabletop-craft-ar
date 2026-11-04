// [hololens] The HoloLens 2 stand-in for OVRCameraRig: a head (the camera) and two hand transforms the app aims rays
// with and sends as the giant's hands. Built by TabletopCraft > HoloLens 2 > Create Scene (Editor/HoloLens2Setup.cs).
//   Head       follows the device through UnityEngine.XR (no extra package); the camera clears to black, which is
//              see-through on an additive display.
//   Left/Right sit at the palm and point from the shoulder through the hand (HoloLensInput sets them every frame),
//              the way HoloLens far rays work, so the block ray, menus and cards work as on Quest.
// The class exists in every build (TabletopApp looks for it); it only does anything with TABLETOP_HL2.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace TabletopCraft.XR
{
    public sealed class HoloLensRig : MonoBehaviour
    {
        public Transform Head, LeftHand, RightHand;

#if TABLETOP_HL2
        readonly List<XRNodeState> _nodes = new List<XRNodeState>();

        void OnEnable() => Application.onBeforeRender += UpdateHead;
        void OnDisable() => Application.onBeforeRender -= UpdateHead;
        void Update() => UpdateHead();

        /// <summary>The head from the XR runtime, right before rendering too so holograms stay put while you move.</summary>
        void UpdateHead()
        {
            if (!Head) return;
            InputTracking.GetNodeStates(_nodes);
            foreach (var n in _nodes)
            {
                if (n.nodeType != XRNode.CenterEye && n.nodeType != XRNode.Head) continue;
                if (n.TryGetPosition(out var p)) Head.localPosition = p;
                if (n.TryGetRotation(out var r)) Head.localRotation = r;
                if (n.nodeType == XRNode.CenterEye) break;
            }
        }
#endif
    }
}
