// [hololens] Engine-free tests for HoloLens 2 input (World/PadInput.cs): hand gestures as controller inputs (pinches,
// fist, the pinch-and-move stick, hysteresis), spoken commands as button presses and stick pushes, the frame edges
// the app's own button logic sees (TapHoldButton, HoldButton), the aim ray, and the device profile.
using System;
using System.Collections.Generic;
using System.Linq;
using TabletopCraft.World;

static class PadTests
{
    static readonly PadVec3 Right = new PadVec3(1, 0, 0), Forward = new PadVec3(0, 0, 1);

    static PadVec3 P(float x, float y, float z) => new PadVec3(x, y, z);

    /// <summary>An open hand, palm at the origin, fingers pointing along +Z.</summary>
    static HandPose Open() => new HandPose
    {
        Tracked = true, Palm = P(0, 0, 0), ThumbTip = P(0.06f, 0, 0.03f),
        IndexTip = P(0.02f, 0, 0.09f), MiddleTip = P(0, 0, 0.095f), RingTip = P(-0.02f, 0, 0.09f), LittleTip = P(-0.04f, 0, 0.075f),
    };

    static HandPose Thumb(HandPose h, PadVec3 at) { h.ThumbTip = at; return h; }

    static HandPose Moved(HandPose h, PadVec3 d)
    {
        h.Palm += d; h.ThumbTip += d; h.IndexTip += d; h.MiddleTip += d; h.RingTip += d; h.LittleTip += d;
        return h;
    }

    static HandPose Curled(float tipDistance) => new HandPose
    {
        Tracked = true, Palm = P(0, 0, 0), ThumbTip = P(0.06f, 0.03f, 0),
        IndexTip = P(0, 0, tipDistance), MiddleTip = P(0, 0, tipDistance), RingTip = P(0, 0, tipDistance), LittleTip = P(0, 0, tipDistance),
    };

    public static void Run(Action<bool, string> check)
    {
        Gestures(check);
        Voice(check);
        Edges(check);
        Aim(check);
        Profile(check);
    }

    static void Gestures(Action<bool, string> check)
    {
        var g = new HandGestureTracker();
        var o = g.Update(Open(), Right, Forward);
        check(o.Tracked && !o.Trigger && !o.Grip && !o.StickClick && !o.StickActive, "hl2: an open hand presses nothing");

        var open = Open();
        o = g.Update(Thumb(open, open.IndexTip + P(0.03f, 0, 0)), Right, Forward);
        check(!o.Trigger, "hl2: thumb 3 cm from the index tip does not start a pinch");
        o = g.Update(Thumb(open, open.IndexTip + P(0.01f, 0, 0)), Right, Forward);
        check(o.Trigger && !o.Grip && !o.StickActive, "hl2: index and thumb together = trigger (air tap)");
        o = g.Update(Thumb(open, open.IndexTip + P(0.03f, 0, 0)), Right, Forward);
        check(o.Trigger, "hl2: a pinch opening to 3 cm is still held (hysteresis)");
        o = g.Update(Thumb(open, open.IndexTip + P(0.045f, 0, 0)), Right, Forward);
        check(!o.Trigger, "hl2: a pinch opening past 4 cm releases");

        // The middle tip is closest to the thumb, the index tip is under the threshold too: the closest finger wins.
        g.Reset();
        var mid = Thumb(open, open.MiddleTip + P(0.005f, 0, 0));
        o = g.Update(mid, Right, Forward);
        check(o.StickActive && !o.Trigger && Math.Abs(o.StickX) < 1e-4f && Math.Abs(o.StickY) < 1e-4f,
              "hl2: middle pinch starts the stick at the centre (closest finger wins over the index)");
        o = g.Update(Moved(mid, P(0.05f, 0, 0)), Right, Forward);
        check(Math.Abs(o.StickX - (0.05f - 0.015f) / (0.10f - 0.015f)) < 1e-3f && Math.Abs(o.StickY) < 1e-4f,
              $"hl2: moving the pinch 5 cm right pushes the stick right ({o.StickX:F3})");
        o = g.Update(Moved(mid, P(0, 0, 0.2f)), Right, Forward);
        check(o.StickY == 1f && Math.Abs(o.StickX) < 1e-4f, "hl2: 20 cm forward is full stick forward (clamped)");
        o = g.Update(Moved(mid, P(-0.01f, 0, 0)), Right, Forward);
        check(o.StickX == 0f, "hl2: 1 cm of drift is inside the dead zone");
        o = g.Update(Moved(mid, P(0.05f, 0, 0)), Forward, P(-1, 0, 0));
        check(o.StickY < -0.4f && o.StickX < 1e-3f, "hl2: the stick follows where the head faces, not world axes");
        o = g.Update(Open(), Right, Forward);
        check(!o.StickActive && o.StickX == 0f, "hl2: letting go centres the stick");

        g.Reset();
        o = g.Update(Thumb(open, open.RingTip + P(0.005f, 0, 0)), Right, Forward);
        check(o.StickClick && !o.StickActive && !o.Trigger, "hl2: ring pinch = stick click");

        g.Reset();
        var fist = new HandPose
        {
            Tracked = true, Palm = P(0, 0, 0), ThumbTip = P(0.03f, 0.01f, 0.03f),
            IndexTip = P(0.02f, 0, 0.03f), MiddleTip = P(0, 0, 0.03f), RingTip = P(-0.02f, 0, 0.03f), LittleTip = P(-0.035f, 0, 0.025f),
        };
        o = g.Update(fist, Right, Forward);
        check(o.Grip && !o.Trigger, "hl2: a fist is the grip and never a pinch, though the thumb touches the index");
        o = g.Update(Curled(0.065f), Right, Forward);
        check(o.Grip, "hl2: a fist opening a little is still held (hysteresis)");
        o = g.Update(Curled(0.08f), Right, Forward);
        check(!o.Grip, "hl2: an open hand releases the grip");
        o = g.Update(Curled(0.065f), Right, Forward);
        check(!o.Grip, "hl2: a half-curled hand does not start a grip");

        g.Update(Thumb(open, open.IndexTip + P(0.01f, 0, 0)), Right, Forward);
        o = g.Update(new HandPose { Tracked = false }, Right, Forward);
        check(!o.Tracked && !o.Trigger && g.Pinch == PinchKind.None, "hl2: a lost hand lets go of everything");
    }

    static void Voice(Action<bool, string> check)
    {
        var phrases = VoiceCommandTable.Phrases();
        check(phrases.Length == phrases.Select(p => p.ToLowerInvariant()).Distinct().Count(), "hl2: every voice phrase is different");
        check(phrases.All(p => p.Trim().Split(' ').Length >= 2), "hl2: every phrase has two or more words (steadier recognition)");
        check(phrases.All(p => p != "select" && p != "go home"), "hl2: no phrase is a HoloLens system command");
        check(VoiceCommandTable.Find("  Place World ")?.Button == PadButton.One && VoiceCommandTable.Find("dig a hole") == null && VoiceCommandTable.Find("") == null,
              "hl2: phrases match ignoring case and spaces; unknown phrases do nothing");
        check(VoiceCommandTable.Find("undo that").Side == PadSide.Right && VoiceCommandTable.Find("undo that").Button == PadButton.Two,
              "hl2: \"undo that\" is B held");
        check(VoiceCommandTable.Find("area setup").Side == PadSide.Left && VoiceCommandTable.Find("area setup").Button == PadButton.Two,
              "hl2: \"area setup\" is Y");
    }

    /// <summary>Frames at 60 Hz with the pad read by the app's own button logic.</summary>
    static (int taps, int holds) RunTapHold(string phrase, double seconds)
    {
        var pad = new PadState(); var voice = new VirtualPad(); var b = new TapHoldButton();
        voice.Fire(phrase, 0);
        int taps = 0, holds = 0;
        for (int i = 0; i <= (int)(seconds * 60); i++)
        {
            double t = i / 60.0;
            pad.Advance(default, default, voice, t);
            var e = b.Update(pad.Get(PadSide.Right, PadButton.Two), t);
            if (e == ButtonEvent.Tap) taps++;
            if (e == ButtonEvent.Hold) holds++;
        }
        return (taps, holds);
    }

    static void Edges(Action<bool, string> check)
    {
        var pick = RunTapHold("pick block", 1.5);
        check(pick.taps == 1 && pick.holds == 0, "hl2: \"pick block\" is one B tap (BlockEditMode picks)");
        var undo = RunTapHold("undo that", 1.5);
        check(undo.holds == 1 && undo.taps == 0, $"hl2: \"undo that\" is exactly one B hold, no tap (taps {undo.taps}, holds {undo.holds})");

        {
            var pad = new PadState(); var voice = new VirtualPad(); var a = new HoldButton { HoldSeconds = 0.5f };
            voice.Fire("place world", 0);
            int fired = 0;
            for (int i = 0; i <= 90; i++)
            {
                double t = i / 60.0;
                pad.Advance(default, default, voice, t);
                if (a.Update(pad.Get(PadSide.Right, PadButton.One), (float)t, false) == HoldButton.Event.Fired) fired++;
            }
            check(fired == 1, "hl2: \"place world\" holds A long enough to re-place the table once (TablePlacement)");
        }
        {
            var pad = new PadState(); var voice = new VirtualPad();
            voice.Fire("pick block", 0);
            pad.Advance(default, default, voice, 0.5);   // one slow frame, after the tap already ended
            check(pad.Get(PadSide.Right, PadButton.Two) && pad.Down(PadSide.Right, PadButton.Two), "hl2: a spoken tap is seen even when the next frame comes late");
            pad.Advance(default, default, voice, 0.52);
            check(!pad.Get(PadSide.Right, PadButton.Two) && pad.Up(PadSide.Right, PadButton.Two) && voice.Active == 0, "hl2: then it is released");
        }
        {
            var pad = new PadState(); var voice = new VirtualPad();
            voice.Fire("open menu", 0);
            pad.Advance(default, default, voice, 0);
            bool first = pad.Down(PadSide.Left, PadButton.Start);
            pad.Advance(default, default, voice, 1 / 60.0);
            check(first && pad.Get(PadSide.Left, PadButton.Start) && !pad.Down(PadSide.Left, PadButton.Start) && !pad.Get(PadSide.Right, PadButton.Start),
                  "hl2: GetDown is true on the first frame only, on the left side only");
        }
        {
            var pad = new PadState(); var voice = new VirtualPad();
            voice.Fire("glide forward", 0); voice.Fire("glide forward", 0.1);
            check(voice.Active == 1, "hl2: saying a push again restarts it instead of stacking");
            var gestureStick = new HandOutput { Tracked = true, StickActive = true, StickX = 0.3f, StickY = -0.5f };
            pad.Advance(gestureStick, default, voice, 0.2);
            check(pad.StickY(PadSide.Left) == 1f && pad.StickX(PadSide.Left) == 0f && pad.StickY(PadSide.Right) == 0f, "hl2: a spoken push wins over the hand's stick, on its side only");
            voice.Fire("stop moving", 0.3);
            pad.Advance(gestureStick, default, voice, 0.3);
            check(voice.Active == 0 && pad.StickY(PadSide.Left) == -0.5f, "hl2: \"stop moving\" ends spoken pushes; the hand's stick is back");
        }
        {
            var pad = new PadState(); var voice = new VirtualPad();
            voice.Fire("go up", 0);
            pad.Advance(default, default, voice, 0.1);
            check(pad.Get(PadSide.Left, PadButton.Grip) && pad.StickY(PadSide.Left) == 1f && !pad.Get(PadSide.Right, PadButton.Grip),
                  "hl2: \"go up\" is left grip + stick up (NavController's vertical glide)");
        }
        {
            var pad = new PadState();
            pad.Advance(new HandOutput { Tracked = true, Trigger = true }, new HandOutput { Tracked = false }, null, 0);
            check(pad.Get(PadSide.Left, PadButton.Trigger) && pad.Tracked(PadSide.Left) && !pad.Tracked(PadSide.Right),
                  "hl2: gestures alone work without voice, and tracking is per hand");
        }
    }

    static void Aim(Action<bool, string> check)
    {
        var head = P(0, 1.6f, 0);
        var sr = AimMath.Shoulder(head, Right, PadSide.Right);
        var sl = AimMath.Shoulder(head, Right, PadSide.Left);
        check(sr.X > 0 && sl.X < 0 && sr.Y < head.Y && Math.Abs(sr.Y - sl.Y) < 1e-5f, "hl2: shoulders sit either side of and below the head");
        var d = AimMath.Direction(sr, sr + P(0, -0.3f, 0.4f));
        check(Math.Abs(d.Length - 1f) < 1e-4f && d.Z > 0 && d.Y < 0, "hl2: the ray points from the shoulder through the hand");
    }

    static void Profile(Action<bool, string> check)
    {
        var h = DeviceProfile.HoloLens2;
        check(h.QuadBudget > 0 && h.QuadBudget < AreaMath.DefaultQuadBudget, "hl2: HoloLens 2 gets a smaller face budget than Quest 3");
        check(h.MinLight > 0 && h.MinLight < 1 && h.MeshWorkers >= 1, "hl2: brighter darks for the additive display, fewer meshing threads");
        check(DeviceProfile.Quest.QuadBudget == 0 && DeviceProfile.Quest.MinLight < 0 && DeviceProfile.Quest.MeshWorkers == 0,
              "hl2: the Quest profile changes nothing");
    }
}
