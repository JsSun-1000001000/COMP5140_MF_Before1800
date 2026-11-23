using UnityEngine;

namespace Before1800.PartB
{
    public sealed class ClockRehearsal
    {
        enum Playback { Idle, Baseline, Replay }

        readonly SessionState state;
        readonly GreyboxWorld world;
        Playback playback;
        float elapsed, cueTime, noticeTime;
        bool playerWatched, linWatched, witnessWasPossible;
        string notice = "";

        public ClockRehearsalProps Props { get; }
        public bool IsPlaying => playback != Playback.Idle;
        public bool CueVisible => cueTime > 0f && !(world.Tv && world.Tv.HasPrediction);

        internal ClockRehearsal(SessionState state, GreyboxWorld world, ClockRehearsalProps props)
        {
            this.state = state;
            this.world = world;
            Props = props;
            Reset();
        }

        internal void Reset()
        {
            playback = Playback.Idle;
            elapsed = cueTime = noticeTime = 0f;
            notice = "";
            Props.ResetProps();
        }

        internal void EndRun()
        {
            playback = Playback.Idle;
            elapsed = cueTime = noticeTime = 0f;
            Props.FinishPlayback();
            Props.ShowCue(false);
        }

        internal bool AtRemote => !state.ClockRemoteTaken && Props.AimingAtRemote(world.ViewCamera, world.Player);
        internal bool AtClock => Props.AimingAtClock(world.ViewCamera, world.Player);
        internal bool AtCard => CueVisible && Props.AimingAtTV(world.ViewCamera, world.Player);

        internal string ClockHint => IsPlaying ? "" : !state.ClockBaselineSeen ? "听厨房挂钟走一轮" :
            state.ClockRemoteTaken ? "用遥控器重播三拍" : "再听一次挂钟";

        internal string Notice
        {
            get
            {
                if (IsPlaying)
                {
                    if (playback == Playback.Baseline) return "看着秒针，听完这三拍。";
                    if (!witnessWasPossible) return "看着表盘，等第三声落下。";
                    return linWatched ? "林正看着表盘。给她留出视线。" : "林错过了一拍，这一遍可以重来。";
                }
                if (noticeTime > 0f) return notice;
                if (state.ClockSoloReplaySeen && !state.ClockWitnessed && AtClock &&
                    (state.ClockInvited || state.Cooperation == CooperationMode.Following))
                    return Props.CanWitness(world.Lin) ? "林看得见表盘了。现在可以按重播。" :
                        "带林走进厨房，在挂钟前稍稍站开，让她看清表盘。";
                if (!state.ClockRemoteTaken && CanSee(Props.RemoteTarget, 3f, 40f))
                    return "茶几上放着一只遥控器。";
                return "";
            }
        }

        internal void TakeRemote()
        {
            if (state.IsPaused || !AtRemote) return;
            state.TakeClockRemote();
            Props.SetRemoteTaken(true);
            Tell("遥控器背面写着「厨房 · 重播」。去厨房看看挂钟。");
        }

        internal void UseClock()
        {
            if (state.IsPaused || IsPlaying || !AtClock) return;
            playback = state.ClockBaselineSeen && state.ClockRemoteTaken ? Playback.Replay : Playback.Baseline;
            elapsed = 0f;
            playerWatched = true;
            witnessWasPossible = playback == Playback.Replay && state.ClockSoloReplaySeen &&
                state.Cooperation != CooperationMode.Withdrawn && Props.CanWitness(world.Lin);
            linWatched = witnessWasPossible;
            Present();
        }

        internal void ReadCard()
        {
            if (state.IsPaused || !AtCard) return;
            state.ReadClockCard();
            Tell("你记下了电视上的重拍提示。");
        }

        internal void Step(float dt)
        {
            if (state.IsPaused || dt <= 0f) return;
            noticeTime = Mathf.Max(0f, noticeTime - dt);
            if (cueTime > 0f)
            {
                if (CueVisible) cueTime = Mathf.Max(0f, cueTime - dt);
                Props.ShowCue(CueVisible);
            }
            if (!IsPlaying) return;
            playerWatched &= CanSee(Props.ClockTarget, 4f, 42f);
            if (linWatched) linWatched = Props.CanWitness(world.Lin);
            elapsed = Mathf.Min(elapsed + dt, ClockRehearsalProps.Duration);
            Present();
            if (elapsed < ClockRehearsalProps.Duration) return;
            if (playback == Playback.Baseline)
            {
                if (playerWatched) state.FinishClockBaseline();
                Tell(!playerWatched ? "你没看完。对准挂钟，可以再听一遍。" :
                    state.ClockRemoteTaken ? "第三拍慢了半拍。对着钟按遥控器的重播键。" :
                    "第三拍慢了半拍。客厅茶几上有一只遥控器。");
            }
            else
            {
                bool alreadyWitnessed = state.ClockWitnessed;
                state.FinishClockReplay(playerWatched, linWatched);
                if (state.ClockWitnessed && (linWatched || playerWatched))
                {
                    cueTime = 24f;
                    Props.ShowCue(CueVisible);
                    Tell(!CueVisible ? "电视预测实验结束后，去看看屏幕。林也有话想说。" :
                        alreadyWitnessed ? "客厅电视上又亮起了那张重拍提示卡。" :
                        "客厅电视亮了。林想问你一件事。");
                }
                else if (!state.ClockSoloReplaySeen) Tell("你没看完这一遍。对准表盘重播。");
                else Tell(!state.ClockInvited && state.Cooperation != CooperationMode.Following ? "三拍原样重来。去把你看到的告诉林。" :
                    "林没看全。让她面向挂钟、避开遮挡，再重播一次。");
            }
            Props.FinishPlayback();
            playback = Playback.Idle;
        }

        bool CanSee(Transform target, float range, float angle)
        {
            var view = world.ViewCamera.transform;
            var delta = target.position - view.position;
            return delta.sqrMagnitude <= range * range && Vector3.Angle(view.forward, delta) < angle &&
                InteractionSight.Clear(view.position, delta, world.Player, target);
        }

        void Present()
        {
            if (playback == Playback.Baseline) Props.ShowBaseline(elapsed);
            else Props.ShowReplay(elapsed);
        }

        void Tell(string line) { notice = line; noticeTime = 6f; }
    }
}
