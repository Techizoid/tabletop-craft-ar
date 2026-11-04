// [hololens] HoloLens 2 input: tracked hands (XR Hands package over OpenXR's hand tracking) and spoken commands
// (Windows keyword recognition) turned into the controller inputs the app reads through TInput. The mapping itself is
// engine-free in World/PadInput.cs (tested in Tests~/PadTests.cs); this file only feeds it joints and phrases.
//   Air tap (index + thumb) = trigger, fist = grip, middle pinch and move = thumbstick, ring pinch = stick click,
//   and phrases for A, B, X, Y and the menu button ("place world", "undo that", "open menu"...; VoiceCommandTable).
// Runs first every frame (DefaultExecutionOrder) so every script sees the same pad state, like OVRInput.Update.
using UnityEngine;
#if TABLETOP_HL2
using System.Collections.Generic;
using TabletopCraft.World;
using UnityEngine.Windows.Speech;
using UnityEngine.XR.Hands;
#endif

namespace TabletopCraft.XR
{
    [DefaultExecutionOrder(-1000)]
    public sealed class HoloLensInput : MonoBehaviour
    {
        public HoloLensRig Rig;
        /// <summary>The last phrase heard and when (for the HUD and logs).</summary>
        public string LastPhrase = "";
        public float LastPhraseAt = -1;
        /// <summary>Set false to turn voice off (tabletop.json "voice": false).</summary>
        public bool Voice = true;

#if TABLETOP_HL2
        public static HoloLensInput Instance { get; private set; }
        public readonly PadState Pad = new PadState();
        public readonly VirtualPad Spoken = new VirtualPad();

        readonly HandGestureTracker _left = new HandGestureTracker(), _right = new HandGestureTracker();
        readonly List<XRHandSubsystem> _subsystems = new List<XRHandSubsystem>();
        XRHandSubsystem _hands;
        KeywordRecognizer _speech;
        readonly object _heardLock = new object();
        readonly Queue<string> _heard = new Queue<string>();

        void Awake() => Instance = this;

        void Start()
        {
            if (!Voice) return;
            try
            {
                _speech = new KeywordRecognizer(VoiceCommandTable.Phrases(), ConfidenceLevel.Medium);
                _speech.OnPhraseRecognized += OnPhrase;
                _speech.Start();
                Debug.Log("[Tabletop] VOICE listening for " + VoiceCommandTable.All.Length + " phrases");
            }
            catch (System.Exception e) { Debug.LogWarning("[Tabletop] VOICE off (microphone capability?): " + e.Message); }
        }

        // The recognizer may call back off the main thread: queue the phrase and handle it in Update.
        void OnPhrase(PhraseRecognizedEventArgs a) { lock (_heardLock) _heard.Enqueue(a.text); }

        void Update()
        {
            double now = Time.unscaledTimeAsDouble;
            lock (_heardLock)
                while (_heard.Count > 0)
                {
                    string t = _heard.Dequeue();
                    Spoken.Fire(t, now);
                    LastPhrase = t; LastPhraseAt = Time.unscaledTime;
                    Debug.Log("[Tabletop] VOICE " + t);
                }

            Transform head = Rig != null ? Rig.Head : null;
            Vector3 fwd = head ? Vector3.ProjectOnPlane(head.forward, Vector3.up) : Vector3.forward;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            var fR = V(right); var fF = V(fwd);

            var subsystem = Hands();
            HandPose lp = default, rp = default;
            if (subsystem != null) { lp = Read(subsystem.leftHand); rp = Read(subsystem.rightHand); }
            var lo = _left.Update(lp, fR, fF);
            var ro = _right.Update(rp, fR, fF);
            Pad.Advance(lo, ro, Spoken, now);

            if (Rig != null && head != null)
            {
                Aim(Rig.LeftHand, lp, head, PadSide.Left);
                Aim(Rig.RightHand, rp, head, PadSide.Right);
            }
        }

        XRHandSubsystem Hands()
        {
            if (_hands != null && _hands.running) return _hands;
            _hands = null;
            SubsystemManager.GetSubsystems(_subsystems);
            foreach (var s in _subsystems) if (s.running) { _hands = s; break; }
            return _hands;
        }

        static HandPose Read(XRHand hand)
        {
            var h = new HandPose { Tracked = hand.isTracked };
            if (!h.Tracked) return h;
            bool ok = J(hand, XRHandJointID.Palm, out h.Palm) & J(hand, XRHandJointID.ThumbTip, out h.ThumbTip) &
                      J(hand, XRHandJointID.IndexTip, out h.IndexTip) & J(hand, XRHandJointID.MiddleTip, out h.MiddleTip) &
                      J(hand, XRHandJointID.RingTip, out h.RingTip) & J(hand, XRHandJointID.LittleTip, out h.LittleTip);
            h.Tracked = ok;
            return h;
        }

        // Joint poses are in the XR session's space; the rig sits at the origin, so that is world space.
        static bool J(XRHand hand, XRHandJointID id, out PadVec3 p)
        {
            p = default;
            if (!hand.GetJoint(id).TryGetPose(out Pose pose)) return false;
            p = V(pose.position);
            return true;
        }

        /// <summary>
        /// The hand transform: at the palm, pointing from the shoulder through it (a far ray). A hand that is down follows
        /// the head instead, so the right ray becomes gaze, as on the first HoloLens: look at a block, raise a hand, tap.
        /// </summary>
        static void Aim(Transform t, HandPose h, Transform head, PadSide side)
        {
            if (!t) return;
            if (!h.Tracked) { t.SetPositionAndRotation(head.position, head.rotation); return; }
            var palm = new Vector3(h.Palm.X, h.Palm.Y, h.Palm.Z);
            var dir = AimMath.Direction(AimMath.Shoulder(V(head.position), V(head.right), side), h.Palm);
            var d = new Vector3(dir.X, dir.Y, dir.Z);
            t.SetPositionAndRotation(palm, d.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(d, Vector3.up) : head.rotation);
        }

        static PadVec3 V(Vector3 v) => new PadVec3(v.x, v.y, v.z);

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_speech != null)
            {
                if (_speech.IsRunning) _speech.Stop();
                _speech.OnPhraseRecognized -= OnPhrase;
                _speech.Dispose();
            }
        }
#endif
    }
}
