// The one place the app asks the controllers what is pressed. Same shape as the part of OVRInput the app uses, so call
// sites read the same:
//   Quest 3 (default build)      forwards to OVRInput, the controllers or Meta's hand tracking (nothing changes)
//   HoloLens 2 (TABLETOP_HL2)    reads the hands-and-voice pad (World/PadInput.cs, XR/HoloLensInput.cs); no haptics
// TABLETOP_HL2 is set for the Universal Windows Platform build group only (TabletopCraft > HoloLens 2 > Configure).
// With it set, nothing here touches OVRInput or any Meta native code, which does not exist on HoloLens.
using TabletopCraft.World;
using UnityEngine;

namespace TabletopCraft.XR
{
    public static class TInput
    {
        /// <summary>The OVRInput buttons the app uses (same names). Two = B on the right, Y on the left.</summary>
        public enum Button { One, Two, Start, PrimaryIndexTrigger, PrimaryHandTrigger, PrimaryThumbstick }

        public enum Controller { LTouch, RTouch, LHand, RHand }

        public enum Axis2D { PrimaryThumbstick }

#if !TABLETOP_HL2
        static OVRInput.Button Map(Button b)
        {
            switch (b)
            {
                case Button.One: return OVRInput.Button.One;
                case Button.Two: return OVRInput.Button.Two;
                case Button.Start: return OVRInput.Button.Start;
                case Button.PrimaryIndexTrigger: return OVRInput.Button.PrimaryIndexTrigger;
                case Button.PrimaryHandTrigger: return OVRInput.Button.PrimaryHandTrigger;
                default: return OVRInput.Button.PrimaryThumbstick;
            }
        }

        static OVRInput.Controller Map(Controller c)
        {
            switch (c)
            {
                case Controller.LTouch: return OVRInput.Controller.LTouch;
                case Controller.RTouch: return OVRInput.Controller.RTouch;
                case Controller.LHand: return OVRInput.Controller.LHand;
                default: return OVRInput.Controller.RHand;
            }
        }

        public static bool Get(Button b, Controller c) => OVRInput.Get(Map(b), Map(c));
        public static bool GetDown(Button b, Controller c) => OVRInput.GetDown(Map(b), Map(c));
        public static bool GetUp(Button b, Controller c) => OVRInput.GetUp(Map(b), Map(c));
        public static Vector2 Get(Axis2D a, Controller c) => OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, Map(c));
        public static void SetControllerVibration(float frequency, float amplitude, Controller c) => OVRInput.SetControllerVibration(frequency, amplitude, Map(c));
        public static bool GetControllerPositionTracked(Controller c) => OVRInput.GetControllerPositionTracked(Map(c));
#else
        static PadSide Side(Controller c) => c == Controller.LTouch || c == Controller.LHand ? PadSide.Left : PadSide.Right;

        static PadButton Map(Button b)
        {
            switch (b)
            {
                case Button.One: return PadButton.One;
                case Button.Two: return PadButton.Two;
                case Button.Start: return PadButton.Start;
                case Button.PrimaryIndexTrigger: return PadButton.Trigger;
                case Button.PrimaryHandTrigger: return PadButton.Grip;
                default: return PadButton.StickClick;
            }
        }

        static PadState Pad
        {
            get { var i = HoloLensInput.Instance; return i != null ? i.Pad : null; }
        }

        public static bool Get(Button b, Controller c) { var p = Pad; return p != null && p.Get(Side(c), Map(b)); }
        public static bool GetDown(Button b, Controller c) { var p = Pad; return p != null && p.Down(Side(c), Map(b)); }
        public static bool GetUp(Button b, Controller c) { var p = Pad; return p != null && p.Up(Side(c), Map(b)); }

        public static Vector2 Get(Axis2D a, Controller c)
        {
            var p = Pad;
            if (p == null) return Vector2.zero;
            var s = Side(c);
            return new Vector2(p.StickX(s), p.StickY(s));
        }

        /// <summary>HoloLens 2 has no haptics: a press is confirmed by the app's own sound and visuals.</summary>
        public static void SetControllerVibration(float frequency, float amplitude, Controller c) { }

        public static bool GetControllerPositionTracked(Controller c) { var p = Pad; return p != null && p.Tracked(Side(c)); }
#endif
    }
}
