// [hololens] The Quest controllers' inputs for a device without controllers (HoloLens 2): hands and voice stand in for
// the buttons and sticks the app reads. Engine-free so it is tested under .NET (Tests~/PadTests.cs); the Unity side is
// XR/HoloLensInput.cs (hand joints, microphone) behind XR/TInput.cs (the one place the app asks "is this pressed").
//
//   Quest controller          HoloLens 2
//   trigger                   index finger and thumb pinched (air tap; hold = hold)
//   grip                      closed fist
//   thumbstick                middle finger and thumb pinched, then the hand moves: forward/back and left/right from
//                             where the pinch started (frame = where the head faces), up to StickReach for full deflection
//   thumbstick click          ring finger and thumb pinched
//   A, B, X, Y, menu          spoken phrases (VoiceCommandTable), each one a short press or hold
//   thumbstick, grip          also by voice for gliding, turning and zooming (an arm held out is tiring)
using System;
using System.Collections.Generic;

namespace TabletopCraft.World
{
    /// <summary>The controller inputs the app uses, named after what they do. One = A / X, Two = B / Y.</summary>
    public enum PadButton { One, Two, Start, Trigger, Grip, StickClick }

    public enum PadSide { Left, Right }

    /// <summary>A point or direction in metres, in whatever common frame the caller uses (Unity world space).</summary>
    public struct PadVec3
    {
        public float X, Y, Z;
        public PadVec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static PadVec3 operator +(PadVec3 a, PadVec3 b) => new PadVec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static PadVec3 operator -(PadVec3 a, PadVec3 b) => new PadVec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static PadVec3 operator *(PadVec3 a, float k) => new PadVec3(a.X * k, a.Y * k, a.Z * k);
        public static float Dot(PadVec3 a, PadVec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        public PadVec3 Normalized { get { float l = Length; return l > 1e-6f ? new PadVec3(X / l, Y / l, Z / l) : new PadVec3(0, 0, 0); } }
        public static float Distance(PadVec3 a, PadVec3 b) => (a - b).Length;
        public static PadVec3 Mid(PadVec3 a, PadVec3 b) => new PadVec3((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f, (a.Z + b.Z) * 0.5f);
    }

    /// <summary>The joints the gestures need for one hand in one frame (Unity world space, metres).</summary>
    public struct HandPose
    {
        public bool Tracked;
        public PadVec3 Palm, ThumbTip, IndexTip, MiddleTip, RingTip, LittleTip;
    }

    /// <summary>Distances in metres. Hysteresis (engage below, release above) keeps a hand at the edge from flickering.</summary>
    public sealed class GestureSettings
    {
        /// <summary>Thumb tip to finger tip: the pinch closes below Engage and opens above Release.</summary>
        public float PinchEngage = 0.022f, PinchRelease = 0.040f;
        /// <summary>
        /// Fingertip to palm. A fist needs the index tip AND the mean of the other three below Engage, and stays a fist
        /// until either opens past Release. The classic air tap (other fingers curled, index out to meet the thumb) keeps
        /// the index out, so it is a pinch and never a fist.
        /// </summary>
        public float CurlEngage = 0.055f, CurlRelease = 0.075f;
        /// <summary>Hand travel from where the middle pinch started that is full stick deflection, and the part ignored.</summary>
        public float StickReach = 0.10f, StickDead = 0.015f;
        /// <summary>A hand lost for less than this keeps its last state, so a tracking blip doesn't release a held pinch
        /// and press it again (which would be an extra break or place).</summary>
        public double LostGraceSeconds = 0.15;
    }

    public enum PinchKind { None, Index, Middle, Ring }

    /// <summary>What one hand is doing as controller inputs, for one frame.</summary>
    public struct HandOutput
    {
        public bool Tracked;
        public bool Trigger;      // index pinch
        public bool Grip;         // fist
        public bool StickClick;   // ring pinch
        public bool StickActive;  // middle pinch: StickX / StickY follow the hand
        public float StickX, StickY;   // -1..1, +Y = away from you
    }

    /// <summary>One hand's gesture state: turns joint poses into trigger, grip, stick and stick click.</summary>
    public sealed class HandGestureTracker
    {
        readonly GestureSettings _s;
        PinchKind _pinch;
        bool _fist;
        PadVec3 _stickOrigin;
        HandOutput _last;
        HandPose _lastPose;
        double _lostAt = double.NaN;

        public HandGestureTracker(GestureSettings settings = null) { _s = settings ?? new GestureSettings(); }

        public PinchKind Pinch => _pinch;
        public bool Fist => _fist;
        /// <summary>The last tracked pose: where the hand is shown while a short loss is bridged.</summary>
        public HandPose LastPose => _lastPose;

        public void Reset() { _pinch = PinchKind.None; _fist = false; _last = default; _lostAt = double.NaN; }

        /// <param name="frameRight">Horizontal unit vector to the user's right (from the head's yaw).</param>
        /// <param name="frameForward">Horizontal unit vector the user faces.</param>
        /// <param name="now">Seconds; with it, a loss shorter than LostGraceSeconds keeps the last output. Without it
        /// (NaN), a lost hand lets go at once.</param>
        public HandOutput Update(HandPose h, PadVec3 frameRight, PadVec3 frameForward, double now = double.NaN)
        {
            var o = new HandOutput();
            if (!h.Tracked)
            {
                if (!double.IsNaN(now) && _last.Tracked)
                {
                    if (double.IsNaN(_lostAt)) _lostAt = now;
                    if (now - _lostAt < _s.LostGraceSeconds) return _last;
                }
                Reset();
                return o;
            }
            _lostAt = double.NaN;
            o.Tracked = true;

            float index = PadVec3.Distance(h.IndexTip, h.Palm);
            float rest = (PadVec3.Distance(h.MiddleTip, h.Palm) + PadVec3.Distance(h.RingTip, h.Palm) +
                          PadVec3.Distance(h.LittleTip, h.Palm)) / 3f;
            _fist = _fist ? index < _s.CurlRelease && rest < _s.CurlRelease : index < _s.CurlEngage && rest < _s.CurlEngage;

            // A fist brings the thumb to the fingers too, so a closed hand is a grip and never a pinch.
            if (_fist) _pinch = PinchKind.None;
            else
            {
                float dI = PadVec3.Distance(h.ThumbTip, h.IndexTip);
                float dM = PadVec3.Distance(h.ThumbTip, h.MiddleTip);
                float dR = PadVec3.Distance(h.ThumbTip, h.RingTip);
                if (_pinch != PinchKind.None)
                {
                    float d = _pinch == PinchKind.Index ? dI : _pinch == PinchKind.Middle ? dM : dR;
                    if (d > _s.PinchRelease) _pinch = PinchKind.None;
                }
                if (_pinch == PinchKind.None)
                {
                    // The closest finger under the threshold starts the pinch and keeps it until released.
                    float best = _s.PinchEngage;
                    var kind = PinchKind.None;
                    if (dI < best) { best = dI; kind = PinchKind.Index; }
                    if (dM < best) { best = dM; kind = PinchKind.Middle; }
                    if (dR < best) { kind = PinchKind.Ring; }
                    _pinch = kind;
                    if (kind == PinchKind.Middle) _stickOrigin = PadVec3.Mid(h.ThumbTip, h.MiddleTip);
                }
            }

            o.Grip = _fist;
            o.Trigger = _pinch == PinchKind.Index;
            o.StickClick = _pinch == PinchKind.Ring;
            if (_pinch == PinchKind.Middle)
            {
                o.StickActive = true;
                var d = PadVec3.Mid(h.ThumbTip, h.MiddleTip) - _stickOrigin;
                o.StickX = Deflect(PadVec3.Dot(d, frameRight.Normalized));
                o.StickY = Deflect(PadVec3.Dot(d, frameForward.Normalized));
            }
            _last = o;
            _lastPose = h;
            return o;
        }

        float Deflect(float travel)
        {
            float a = Math.Abs(travel);
            if (a <= _s.StickDead) return 0f;
            float k = Math.Min(1f, (a - _s.StickDead) / (_s.StickReach - _s.StickDead));
            return travel < 0 ? -k : k;
        }
    }

    /// <summary>Where a hand points: from the shoulder through the hand, the way far rays work on HoloLens.</summary>
    public static class AimMath
    {
        /// <summary>The shoulder on that side, from the head (eye) position and the head's right vector.</summary>
        public static PadVec3 Shoulder(PadVec3 head, PadVec3 headRight, PadSide side, float lateral = 0.17f, float below = 0.17f) =>
            head + headRight.Normalized * (side == PadSide.Right ? lateral : -lateral) + new PadVec3(0, -below, 0);

        public static PadVec3 Direction(PadVec3 shoulder, PadVec3 hand) => (hand - shoulder).Normalized;

        /// <summary>
        /// Where a hand that is down is shown: in front of you, low and to its side, like a controller held at rest.
        /// The app hangs things on the hand transforms (the left hand carries the menus and the wrist chat; the right
        /// one the block preview), so they must never sit at the eyes. Directions are horizontal, from the head's yaw.
        /// </summary>
        public static PadVec3 Rest(PadVec3 head, PadVec3 flatForward, PadVec3 flatRight, PadSide side) =>
            head + flatForward.Normalized * (side == PadSide.Right ? 0.30f : 0.35f)
                 + flatRight.Normalized * (side == PadSide.Right ? 0.15f : -0.15f)
                 + new PadVec3(0, side == PadSide.Right ? -0.25f : -0.30f, 0);

        /// <summary>The point a hand that is down aims at: along the gaze, at about table distance.</summary>
        public static PadVec3 GazePoint(PadVec3 head, PadVec3 headForward, float metres = 0.6f) => head + headForward.Normalized * metres;
    }

    /// <summary>A spoken phrase and the controller input it presses for a moment.</summary>
    public sealed class VoiceCommand
    {
        public string Phrase;
        public PadSide Side;
        public bool HasButton;
        public PadButton Button;
        public float Seconds;
        public float StickX, StickY;   // pushed for Seconds
        public bool WithGrip;          // the grip is also held (left grip + stick = up / down)
        public bool StopAll;           // cancels every press still running
    }

    /// <summary>
    /// The phrases the app listens for. Two or more words each: the recognizer is steadier with them, and none of them is
    /// a HoloLens system command ("select", "go home"). Press lengths match the app: a tap is a few frames, A held 0.5 s
    /// re-places the table (HoldButton), B held 0.6 s undoes (TapHoldButton; one undo, the repeat comes at 1.0 s).
    /// </summary>
    public static class VoiceCommandTable
    {
        public const float Tap = 0.15f, PlaceHold = 0.7f, UndoHold = 0.9f, Push = 1.0f, Nudge = 0.6f;

        static VoiceCommand Press(string phrase, PadSide side, PadButton b, float seconds) =>
            new VoiceCommand { Phrase = phrase, Side = side, HasButton = true, Button = b, Seconds = seconds };

        static VoiceCommand Stick(string phrase, PadSide side, float x, float y, float seconds, bool grip = false) =>
            new VoiceCommand { Phrase = phrase, Side = side, StickX = x, StickY = y, Seconds = seconds, WithGrip = grip };

        public static readonly VoiceCommand[] All =
        {
            Press("place world", PadSide.Right, PadButton.One, PlaceHold),   // the table goes where your right hand is
            Press("confirm area", PadSide.Right, PadButton.One, Tap),        // A in area setup
            Press("cancel setup", PadSide.Right, PadButton.Two, Tap),        // B in area setup
            Press("pick block", PadSide.Right, PadButton.Two, Tap),          // B: pick the block under the ray
            Press("undo that", PadSide.Right, PadButton.Two, UndoHold),      // B held: undo
            Press("open menu", PadSide.Left, PadButton.Start, Tap),          // the menu button toggles the block menu
            Press("close menu", PadSide.Left, PadButton.Start, Tap),
            Press("operator panel", PadSide.Left, PadButton.One, Tap),       // X: chat, console, players
            Press("area setup", PadSide.Left, PadButton.Two, Tap),           // Y: choose the area
            Stick("zoom in", PadSide.Right, 0f, 1f, Push),                   // right stick up: bigger miniature
            Stick("zoom out", PadSide.Right, 0f, -1f, Push),
            Stick("turn left", PadSide.Right, -1f, 0f, Nudge),
            Stick("turn right", PadSide.Right, 1f, 0f, Nudge),
            Stick("glide forward", PadSide.Left, 0f, 1f, Push),
            Stick("glide back", PadSide.Left, 0f, -1f, Push),
            Stick("glide left", PadSide.Left, -1f, 0f, Push),
            Stick("glide right", PadSide.Left, 1f, 0f, Push),
            Stick("go up", PadSide.Left, 0f, 1f, Push, grip: true),
            Stick("go down", PadSide.Left, 0f, -1f, Push, grip: true),
            new VoiceCommand { Phrase = "stop moving", StopAll = true },
        };

        public static string[] Phrases()
        {
            var p = new string[All.Length];
            for (int i = 0; i < p.Length; i++) p[i] = All[i].Phrase;
            return p;
        }

        /// <summary>The command for what the recognizer heard (case and surrounding space ignored); null if none.</summary>
        public static VoiceCommand Find(string heard)
        {
            if (string.IsNullOrWhiteSpace(heard)) return null;
            string t = heard.Trim();
            foreach (var c in All) if (string.Equals(c.Phrase, t, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }
    }

    /// <summary>The spoken presses in progress. A press is always seen by at least one frame, however short.</summary>
    public sealed class VirtualPad
    {
        sealed class Pulse { public VoiceCommand Cmd; public double Until; public bool Delivered; }
        readonly List<Pulse> _active = new List<Pulse>();

        public int Active => _active.Count;

        public void Fire(VoiceCommand c, double now)
        {
            if (c == null) return;
            if (c.StopAll) { _active.Clear(); return; }
            // The same input pressed again restarts it rather than stacking.
            _active.RemoveAll(p => p.Cmd.Side == c.Side && p.Cmd.HasButton == c.HasButton && p.Cmd.Button == c.Button &&
                                   p.Cmd.StickX == c.StickX && p.Cmd.StickY == c.StickY && p.Cmd.WithGrip == c.WithGrip);
            _active.Add(new Pulse { Cmd = c, Until = now + c.Seconds });
        }

        public void Fire(string heard, double now) => Fire(VoiceCommandTable.Find(heard), now);

        /// <summary>Drops the presses that ended and were seen (call at the start of a frame).</summary>
        public void Tick(double now) => _active.RemoveAll(p => p.Until <= now && p.Delivered);

        /// <summary>Marks the presses as seen (call at the end of a frame's read).</summary>
        public void Deliver() { foreach (var p in _active) p.Delivered = true; }

        public bool Button(PadSide side, PadButton b)
        {
            foreach (var p in _active)
                if (p.Cmd.Side == side && ((p.Cmd.HasButton && p.Cmd.Button == b) || (b == PadButton.Grip && p.Cmd.WithGrip))) return true;
            return false;
        }

        /// <summary>The pushed stick of that side, false when no spoken press moves it.</summary>
        public bool Stick(PadSide side, out float x, out float y)
        {
            x = 0; y = 0; bool any = false;
            foreach (var p in _active)
            {
                if (p.Cmd.Side != side || (p.Cmd.StickX == 0f && p.Cmd.StickY == 0f)) continue;
                x += p.Cmd.StickX; y += p.Cmd.StickY; any = true;
            }
            x = Math.Max(-1f, Math.Min(1f, x)); y = Math.Max(-1f, Math.Min(1f, y));
            return any;
        }
    }

    /// <summary>
    /// The pad as the app sees it for one frame: gestures and spoken presses together, with the edges (down, up) that
    /// OVRInput.GetDown / GetUp give. Advance once per frame, before anything reads it.
    /// </summary>
    public sealed class PadState
    {
        const int Buttons = 6;
        readonly bool[,] _cur = new bool[2, Buttons], _prev = new bool[2, Buttons];
        readonly float[] _sx = new float[2], _sy = new float[2];
        readonly bool[] _tracked = new bool[2];

        public void Advance(HandOutput left, HandOutput right, VirtualPad voice, double now)
        {
            voice?.Tick(now);
            for (int s = 0; s < 2; s++)
                for (int b = 0; b < Buttons; b++) _prev[s, b] = _cur[s, b];
            Set(PadSide.Left, left, voice);
            Set(PadSide.Right, right, voice);
            voice?.Deliver();
        }

        void Set(PadSide side, HandOutput h, VirtualPad voice)
        {
            int s = (int)side;
            _tracked[s] = h.Tracked;
            _cur[s, (int)PadButton.Trigger] = h.Trigger;
            _cur[s, (int)PadButton.Grip] = h.Grip || (voice != null && voice.Button(side, PadButton.Grip));
            _cur[s, (int)PadButton.StickClick] = h.StickClick || (voice != null && voice.Button(side, PadButton.StickClick));
            _cur[s, (int)PadButton.One] = voice != null && voice.Button(side, PadButton.One);
            _cur[s, (int)PadButton.Two] = voice != null && voice.Button(side, PadButton.Two);
            _cur[s, (int)PadButton.Start] = voice != null && voice.Button(side, PadButton.Start);
            float x = h.StickActive ? h.StickX : 0f, y = h.StickActive ? h.StickY : 0f;
            if (voice != null && voice.Stick(side, out float vx, out float vy)) { x = vx; y = vy; }   // a spoken push wins
            _sx[s] = x; _sy[s] = y;
        }

        public bool Get(PadSide side, PadButton b) => _cur[(int)side, (int)b];
        public bool Down(PadSide side, PadButton b) => _cur[(int)side, (int)b] && !_prev[(int)side, (int)b];
        public bool Up(PadSide side, PadButton b) => !_cur[(int)side, (int)b] && _prev[(int)side, (int)b];
        public float StickX(PadSide side) => _sx[(int)side];
        public float StickY(PadSide side) => _sy[(int)side];
        /// <summary>Whether a hand of that side is tracked: the avatar's hand follows it only then.</summary>
        public bool Tracked(PadSide side) => _tracked[(int)side];
    }

    /// <summary>
    /// [hololens] What differs by device, read once at start: HoloLens 2 has about a quarter of the Quest 3's GPU and an
    /// additive display where black is clear, so it gets a smaller face budget and brighter darks. The numbers are
    /// starting points (docs/HOLOLENS2.md); tabletop.json "quad_budget" and "min_light" override them.
    /// </summary>
    public sealed class DeviceProfile
    {
        public string Name = "Quest";
        public long QuadBudget;      // faces before area setup asks for the override; 0 = AreaMath.DefaultQuadBudget
        public float MinLight = -1f; // brightness of light level 0; negative = leave BlockShading's default
        public int MeshWorkers;      // 0 = leave as is

        public static readonly DeviceProfile Quest = new DeviceProfile();
        public static readonly DeviceProfile HoloLens2 = new DeviceProfile { Name = "HoloLens 2", QuadBudget = 60_000, MinLight = 0.4f, MeshWorkers = 2 };
    }
}
