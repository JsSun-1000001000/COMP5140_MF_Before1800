using Before1800.PartB;
using UnityEngine;

namespace Before1800.Home
{
    // The character controller owns world motion. Match the original in-place cycle to the ground it covers.
    [RequireComponent(typeof(Animator))]
    public sealed class ResidenceLocomotion : MonoBehaviour
    {
        static readonly int Walk = Animator.StringToHash("Walk");
        static readonly int StrideRate = Animator.StringToHash("StrideRate");
        // Metres per second of the supporting shoe's backward sweep in the clip at 1x.
        const float AuthoredSpeed = .78f;
        [SerializeField] AnimationClip blink;
        Animator animator;
        ResidenceExpression expression;
        GreyboxWorld world;
        Vector3 previousPosition;
        Quaternion previousRotation;
        bool stepping;

        void OnEnable()
        {
            animator = GetComponent<Animator>();
            animator.applyRootMotion = false;
            // The imported walk clip contains footstep callbacks for the original game.
            animator.fireEvents = false;
            previousPosition = transform.parent.position;
            previousRotation = transform.parent.rotation;
            stepping = false;
        }

        public void SetBlinkClip(AnimationClip clip) => blink = clip;

        void Start()
        {
            world = GetComponentInParent<GreyboxWorld>();
            expression = new ResidenceExpression(transform, blink, Camera.main);
        }

        void Update()
        {
            expression?.RestorePose();
            Transform actor = transform.parent;
            Vector3 travel = actor.position - previousPosition;
            float turn = Quaternion.Angle(previousRotation, actor.rotation);
            previousPosition = actor.position;
            previousRotation = actor.rotation;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            travel.y = 0f;
            // For a pivot, the feet still have to step around the body's centre.
            float speed = Mathf.Max(travel.magnitude, turn * Mathf.Deg2Rad * .15f) / dt;
            if (travel.magnitude > .1f + 6f * dt) speed = 0f;
            stepping = speed > (stepping ? .045f : .085f);
            animator.SetBool(Walk, stepping);
            if (stepping)
                animator.SetFloat(StrideRate, Mathf.Clamp(speed / AuthoredSpeed, .35f, 2f));
        }

        float ExpressionStep
        {
            get
            {
                if(Time.timeScale>0f)return Time.deltaTime;
                var scene=world ? world.GetComponent<PartBScene>() : null;
                return scene && scene.DialogueOpen && !scene.MenuOpen ? (Time.captureDeltaTime>0?Time.captureDeltaTime:Time.unscaledDeltaTime) : 0f;
            }
        }

        void LateUpdate() => expression?.Animate(ExpressionStep, stepping,
            world ? world.DesiredFacing : Vector3.zero);

        void OnDisable() => expression?.RestorePose();
    }
}
