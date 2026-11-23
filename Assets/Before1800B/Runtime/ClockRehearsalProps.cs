using UnityEngine;

namespace Before1800.PartB
{
    public sealed class ClockRehearsalProps : MonoBehaviour
    {
        public const float Duration = 6f;

        [SerializeField] Transform clockTarget, remoteTarget, tvTarget, secondsHand;
        [SerializeField] GameObject tableRemote, cueCard;
        [SerializeField] AudioSource clockTick;
        [SerializeField] Quaternion originalHandRotation;
        int lastMark;

        public Transform ClockTarget => clockTarget;
        public Transform RemoteTarget => remoteTarget;
        public Transform TvTarget => tvTarget;
        public Transform SecondsHand => secondsHand;

        public void Configure(Transform clock, Transform remote, Transform television, Transform seconds,
            GameObject remoteProp, GameObject tvCard, AudioSource tick)
        {
            clockTarget = clock;
            remoteTarget = remote;
            tvTarget = television;
            secondsHand = seconds;
            originalHandRotation = seconds.localRotation;
            tableRemote = remoteProp;
            cueCard = tvCard;
            clockTick = tick;
            ResetProps();
        }

        public bool AimingAtClock(Camera camera, Transform player) => InView(camera, player, clockTarget, 3.35f, 14f);
        public bool AimingAtRemote(Camera camera, Transform player) => InView(camera, player, remoteTarget, 2.3f, 16f);
        public bool AimingAtTV(Camera camera, Transform player) => InView(camera, player, tvTarget, 3.2f, 16f);

        static bool InView(Camera camera, Transform player, Transform target, float reach, float cone)
        {
            if (!target || !target.gameObject.activeInHierarchy || !camera) return false;
            Vector3 origin = camera.transform.position;
            Vector3 direction = target.position - origin;
            return direction.sqrMagnitude <= reach * reach && Vector3.Angle(camera.transform.forward, direction) <= cone
                && InteractionSight.Clear(origin, direction, player, target);
        }

        public bool CanWitness(Transform lin)
        {
            if (!lin || !clockTarget) return false;
            var eye = lin.position + Vector3.up * 1.55f;
            var direction = clockTarget.position - eye;
            var bearing = Vector3.ProjectOnPlane(direction, Vector3.up);
            // A four-metre sightline from this east wall lies wholly inside the kitchen.
            return direction.sqrMagnitude <= 16f && Vector3.Angle(lin.forward, bearing) <= 65f
                && InteractionSight.Clear(eye, direction, lin, clockTarget);
        }

        public void SetRemoteTaken(bool taken) => tableRemote.SetActive(!taken);
        public void ShowCue(bool show) => cueCard.SetActive(show);

        public void ShowBaseline(float elapsed)
        {
            // The third beat is late; subsequent beats continue forward.
            int mark = elapsed < 3f ? Mathf.FloorToInt(Mathf.Max(0f, elapsed)) :
                Mathf.Max(2, Mathf.FloorToInt(elapsed - .65f));
            PoseHand(elapsed >= Duration ? 0 : mark);
        }

        public void ShowReplay(float elapsed)
        {
            // Three dial marks, a held beat, then the same three marks again.
            float phrase = elapsed < 3f ? elapsed : elapsed - 3f;
            int mark = Mathf.Clamp(Mathf.FloorToInt((phrase + .28f) / .76f), 0, 3);
            PoseHand(elapsed >= Duration ? 0 : mark);
        }

        void PoseHand(int mark)
        {
            if (lastMark == mark) return;
            lastMark = mark;
            // The source clock's local Y axis is perpendicular to its face.
            secondsHand.localRotation = Quaternion.AngleAxis(-6f * mark, Vector3.up) * originalHandRotation;
            if (mark != 0 && clockTick && clockTick.clip && Application.isPlaying) clockTick.PlayOneShot(clockTick.clip);
        }

        public void FinishPlayback()
        {
            lastMark = 0;
            if (secondsHand) secondsHand.localRotation = originalHandRotation;
            if (clockTick && Application.isPlaying) clockTick.Stop();
        }

        public void ResetProps()
        {
            FinishPlayback();
            ShowCue(false);
            SetRemoteTaken(false);
        }
    }
}
