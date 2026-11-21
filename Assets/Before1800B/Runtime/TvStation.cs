using UnityEngine;
using UnityEngine.AI;

namespace Before1800.PartB
{
    public enum TvProgram { None = 0, News = 1, Cartoon = 2, Weather = 3 }

    public enum TvPhase
    {
        Idle,
        Predicted,
        Running,
        Step1Matched,
        Completed,
        Failed
    }

    // Living-room TV carrier for E4. Prediction locks a fixed event_id; starting the
    // sequence then matching and intervening both succeed before evidence is written.
    public sealed class TvStation : MonoBehaviour
    {
        public const string NewsId = "tv_news";
        public const string CartoonId = "tv_cartoon";
        public const string WeatherId = "tv_weather";

        [SerializeField] Transform screen;
        [SerializeField] Transform observationPoint;
        [SerializeField] Transform control;
        [SerializeField] Renderer screenRenderer;
        [SerializeField] TextMesh caption;

        Material screenMaterial;
        Color idleColor = new Color(.08f, .09f, .11f);

        public Transform ObservationPoint => observationPoint;
        public Transform Control => control ? control : (screen ? screen : transform);
        public Transform Screen => screen ? screen : transform;
        public TvPhase Phase { get; private set; }
        public TvProgram PredictedProgram { get; private set; }
        public TvProgram DisplayedProgram { get; private set; }
        public string PredictedEventId => EventIdFor(PredictedProgram);
        public string DisplayedEventId => EventIdFor(DisplayedProgram);
        public bool IsAvailable => isActiveAndEnabled && observationPoint && screenRenderer &&
            observationPoint.gameObject.activeInHierarchy && Screen.gameObject.activeInHierarchy;
        public bool HasPrediction => Phase == TvPhase.Predicted || Phase == TvPhase.Running ||
            Phase == TvPhase.Step1Matched;
        public bool CanRegisterPrediction => Phase == TvPhase.Idle || Phase == TvPhase.Failed ||
            Phase == TvPhase.Predicted;
        public bool CanStartSequence => Phase == TvPhase.Predicted;
        public bool CanConfirmPredictedShow => Phase == TvPhase.Running;
        public bool CanIntervene => Phase == TvPhase.Step1Matched;
        public bool IsComplete => Phase == TvPhase.Completed;

        public static string EventIdFor(TvProgram program)
        {
            switch (program)
            {
                case TvProgram.News: return NewsId;
                case TvProgram.Cartoon: return CartoonId;
                case TvProgram.Weather: return WeatherId;
                default: return null;
            }
        }

        public static string LabelFor(TvProgram program)
        {
            switch (program)
            {
                case TvProgram.News: return "新闻";
                case TvProgram.Cartoon: return "动画片";
                case TvProgram.Weather: return "天气预报";
                default: return "黑屏";
            }
        }

        // The three PRD examples use these intervene targets.
        public static TvProgram AlternateFor(TvProgram program)
        {
            switch (program)
            {
                case TvProgram.News: return TvProgram.Weather;      // 例1：新闻 → 天气
                case TvProgram.Cartoon: return TvProgram.News;      // 例2：动画 → 新闻
                case TvProgram.Weather: return TvProgram.Cartoon;   // 例3 成功重试：天气 → 动画
                default: return TvProgram.None;
            }
        }

        public static TvProgram ProgramFromEventId(string eventId)
        {
            if (eventId == NewsId) return TvProgram.News;
            if (eventId == CartoonId) return TvProgram.Cartoon;
            if (eventId == WeatherId) return TvProgram.Weather;
            return TvProgram.None;
        }

        public void ResetStation()
        {
            Phase = TvPhase.Idle;
            PredictedProgram = TvProgram.None;
            Show(TvProgram.None);
        }

        public bool TryRegisterPrediction(TvProgram program, out string reason)
        {
            if (!IsAvailable) return Reject("客厅电视还没准备好。", out reason);
            if (Phase == TvPhase.Running || Phase == TvPhase.Step1Matched || Phase == TvPhase.Completed)
                return Reject("序列已启动，不能再改预测。", out reason);
            if (program == TvProgram.None) return Reject("请选择一个固定节目预测。", out reason);
            PredictedProgram = program;
            Phase = TvPhase.Predicted;
            Show(TvProgram.None);
            reason = "已锁定预测：" + LabelFor(program) + "（" + EventIdFor(program) + "）。";
            return true;
        }

        public bool TryStartSequence(out string reason)
        {
            if (!CanStartSequence) return Reject(
                Phase == TvPhase.Idle || Phase == TvPhase.Failed
                    ? "请先登记预测，再启动电视序列。"
                    : "现在不能启动。", out reason);
            Phase = TvPhase.Running;
            // On-air default is never the predicted show, so a wrong request can fail step 1.
            Show(DefaultOnAir(PredictedProgram));
            reason = "电视序列已启动。请林点播你预测的节目。";
            return true;
        }

        // Step 1: Lin requests a show. Only the locked prediction counts.
        public bool TryLinRequestShow(TvProgram requested, out string reason)
        {
            if (Phase != TvPhase.Running) return Reject("还没启动序列，或已经过了点播阶段。", out reason);
            if (requested == TvProgram.None) return Reject("林需要点一个具体节目。", out reason);
            Show(requested);
            if (requested != PredictedProgram || DisplayedProgram != PredictedProgram)
            {
                Phase = TvPhase.Failed;
                reason = "预测不符：电视出了" + LabelFor(DisplayedProgram) + "，不是" +
                    LabelFor(PredictedProgram) + "。不计 E4，可复位重试。";
                return false;
            }
            Phase = TvPhase.Step1Matched;
            reason = "步骤1通过：预测命中，电视正在播" + LabelFor(PredictedProgram) + "。";
            return true;
        }

        // Convenience: request exactly the locked prediction (examples 1 & 2 happy path).
        public bool TryConfirmPredictedShow(out string reason) =>
            TryLinRequestShow(PredictedProgram, out reason);

        // Step 2: Lin changes desire; the TV must follow.
        public bool TryIntervene(out string reason)
        {
            if (Phase != TvPhase.Step1Matched) return Reject("需要先完成预测命中，再改道验证。", out reason);
            TvProgram next = AlternateFor(PredictedProgram);
            if (next == TvProgram.None) return Reject("没有可用的改道节目。", out reason);
            Show(next);
            if (DisplayedProgram != next)
            {
                Phase = TvPhase.Failed;
                reason = "改道后画面没有跟着更新。不计 E4，可复位重试。";
                return false;
            }
            Phase = TvPhase.Completed;
            reason = "步骤2通过：林改口想看" + LabelFor(next) + "，电视跟着换成了它。";
            return true;
        }

        public bool AtObservation(Transform lin)
        {
            if (!IsAvailable || !lin) return false;
            if (Vector3.Distance(lin.position, observationPoint.position) > .75f) return false;
            Vector3 toward = Flat(Screen.position - lin.position);
            return toward.sqrMagnitude < .0001f || Vector3.Angle(lin.forward, toward) <= 45f;
        }

        public bool PlayerCanOperate(Transform view)
        {
            if (!IsAvailable || !view) return false;
            Vector3 origin = view.position;
            Vector3 delta = Control.position - origin;
            if (delta.sqrMagnitude > 3.4f * 3.4f || Vector3.Angle(view.forward, delta) > 36f) return false;
            return InteractionSight.Clear(origin, delta, view.GetComponentInParent<CharacterController>()?.transform, Screen);
        }

        public string DescribeBlock(Transform view, Transform lin, bool linArrived)
        {
            if (!IsAvailable) return "客厅电视还没准备好。";
            if (!HasPrediction && Phase != TvPhase.Completed) return "先和林登记预测（新闻 / 动画片 / 天气预报）。";
            if (!linArrived || !lin || !AtObservation(lin)) return "等林站到电视前的观察点上。";
            if (!view || !PlayerCanOperate(view)) return "走近并对准电视屏幕。";
            if (CanStartSequence) return "";
            if (CanConfirmPredictedShow) return "";
            if (CanIntervene) return "";
            if (Phase == TvPhase.Completed) return "E4 已完成。";
            if (Phase == TvPhase.Failed) return "本次预测失败。可重新登记后重试。";
            return "";
        }

        public bool CanReachObservation()
        {
            if (!IsAvailable) return false;
            return NavMesh.SamplePosition(observationPoint.position, out _, .55f, NavMesh.AllAreas);
        }

        void Show(TvProgram program)
        {
            DisplayedProgram = program;
            EnsureMaterial();
            Color tone = ColorFor(program);
            if (screenMaterial.HasProperty("_Color")) screenMaterial.color = tone;
            if (screenMaterial.HasProperty("_BaseColor")) screenMaterial.SetColor("_BaseColor", tone);
            if (screenMaterial.HasProperty("_EmissionColor"))
            {
                screenMaterial.EnableKeyword("_EMISSION");
                screenMaterial.SetColor("_EmissionColor", program == TvProgram.None ? Color.black : tone * 1.6f);
            }
            if (caption) caption.text = program == TvProgram.None ? "TV  STANDBY" : LabelFor(program).ToUpperInvariant();
        }

        static TvProgram DefaultOnAir(TvProgram predicted)
        {
            // Always a different fixed show so a wrong request can demonstrate example 3.
            switch (predicted)
            {
                case TvProgram.News: return TvProgram.Cartoon;
                case TvProgram.Cartoon: return TvProgram.Weather;
                default: return TvProgram.News;
            }
        }

        static Color ColorFor(TvProgram program)
        {
            switch (program)
            {
                case TvProgram.News: return new Color(.18f, .42f, .78f);
                case TvProgram.Cartoon: return new Color(.86f, .42f, .18f);
                case TvProgram.Weather: return new Color(.18f, .62f, .48f);
                default: return new Color(.08f, .09f, .11f);
            }
        }

        void EnsureMaterial()
        {
            if (screenMaterial || !screenRenderer) return;
            screenMaterial = new Material(screenRenderer.sharedMaterial);
            screenRenderer.sharedMaterial = screenMaterial;
            if (screenMaterial.HasProperty("_Color")) idleColor = screenMaterial.color;
        }

        static bool Reject(string message, out string reason)
        {
            reason = message;
            return false;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // Greybox and residence both call Ensure so authored scenes need no menu rebuild.
        public static TvStation Ensure(Transform worldRoot, Material markMaterial = null)
        {
            if (!worldRoot) return null;
            var existing = worldRoot.GetComponentInChildren<TvStation>(true);
            if (existing && existing.IsAvailable) return existing;

            Transform tvProp = FindNamed(worldRoot, "TVTumb");
            Vector3 screenPos;
            Vector3 observePos;
            Quaternion face;
            if (tvProp)
            {
                var bounds = BoundsOf(tvProp);
                Vector3 facing = Flat(worldRoot.position + Vector3.forward - bounds.center);
                if (facing.sqrMagnitude < .01f) facing = Vector3.back;
                facing.Normalize();
                // Face into the living room: prefer toward the room centre if available.
                Vector3 room = worldRoot.position;
                Vector3 inward = Flat(room - bounds.center);
                if (inward.sqrMagnitude > .01f) facing = inward.normalized;
                screenPos = bounds.center + Vector3.up * .55f + facing * .05f;
                observePos = bounds.center + facing * 1.35f;
                observePos.y = .02f;
                face = Quaternion.LookRotation(facing);
            }
            else
            {
                // Greybox fallback: opposite side of the camera wall so E4 is a distinct walk.
                screenPos = new Vector3(-3.55f, 1.35f, -4.55f);
                observePos = new Vector3(-3.55f, .02f, -2.85f);
                face = Quaternion.LookRotation(Vector3.forward);
            }

            var root = existing ? existing.transform : new GameObject("E4 living-room TV").transform;
            if (!existing)
            {
                root.SetParent(worldRoot, false);
                root.position = screenPos;
                root.rotation = face;
            }
            var station = existing ? existing : root.gameObject.AddComponent<TvStation>();

            if (!station.screen)
            {
                var panel = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
                panel.name = "TV screen";
                panel.SetParent(root, false);
                panel.localPosition = Vector3.zero;
                panel.localScale = new Vector3(1.15f, .68f, .04f);
                station.screen = panel;
                station.screenRenderer = panel.GetComponent<Renderer>();
                var hit = panel.GetComponent<Collider>();
                if (hit) hit.enabled = true;
            }
            if (!station.control) station.control = station.screen;
            if (!station.observationPoint)
            {
                station.observationPoint = new GameObject("e4_tv_observe").transform;
                station.observationPoint.SetParent(worldRoot, false);
                station.observationPoint.position = observePos;
                station.observationPoint.rotation = Quaternion.LookRotation(Flat(screenPos - observePos));
                PaintObserveMark(station.observationPoint, markMaterial);
            }
            if (!station.caption)
            {
                var label = new GameObject("TV caption").AddComponent<TextMesh>();
                label.transform.SetParent(station.screen, false);
                label.transform.localPosition = new Vector3(0f, 0f, -.52f);
                label.transform.localRotation = Quaternion.identity;
                label.characterSize = .045f;
                label.fontSize = 42;
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.color = Color.white;
                station.caption = label;
            }
            station.ResetStation();
            return station.IsAvailable ? station : null;
        }

        static Transform FindNamed(Transform root, string name)
        {
            if (!root) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindNamed(root.GetChild(i), name);
                if (found) return found;
            }
            return null;
        }

        static Bounds BoundsOf(Transform item)
        {
            var collider = item.GetComponent<Collider>();
            if (collider) return collider.bounds;
            var renderer = item.GetComponent<Renderer>();
            if (renderer) return renderer.bounds;
            return new Bounds(item.position, Vector3.one);
        }

        static void PaintObserveMark(Transform mark, Material material)
        {
            if (!mark || mark.childCount > 0) return;
            for (int side = -1; side <= 1; side += 2)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
                bar.name = "TV observation mark";
                bar.SetParent(mark, false);
                bar.localPosition = new Vector3(side * .38f, .01f, 0f);
                bar.localScale = new Vector3(.07f, .012f, .85f);
                if (material) bar.GetComponent<Renderer>().sharedMaterial = material;
                var collider = bar.GetComponent<Collider>();
                if (collider) collider.enabled = false;
            }
        }
    }
}
